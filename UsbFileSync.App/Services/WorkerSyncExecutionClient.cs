using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.Services;

internal interface IWorkerSyncSession : IDisposable
{
    bool IsElevated { get; }

    Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        CancellationToken cancellationToken);
}

internal delegate Task<IWorkerSyncSession> WorkerSyncSessionFactory(bool runElevated, CancellationToken cancellationToken);

internal sealed class WorkerSyncExecutionClient : ISyncExecutionClient, IDisposable
{
    private readonly ISourceVolumeService _destinationVolumeService;
    private readonly WorkerSyncSessionFactory _sessionFactory;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private IWorkerSyncSession? _session;
    private bool _disposed;

    public WorkerSyncExecutionClient()
        : this(
            SyncVolumeServiceFactory.CreateDestinationVolumeService(),
            CreateDefaultSessionFactory(() => Environment.ProcessPath, TimeSpan.FromSeconds(60)))
    {
    }

    internal WorkerSyncExecutionClient(
        ISourceVolumeService destinationVolumeService,
        WorkerSyncSessionFactory sessionFactory)
    {
        _destinationVolumeService = destinationVolumeService ?? throw new ArgumentNullException(nameof(destinationVolumeService));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(actions);

        var runElevated = SyncVolumeServiceFactory.RequiresElevatedWorker(configuration.GetDestinationPaths(), _destinationVolumeService);
        var session = await GetOrCreateSessionAsync(runElevated, cancellationToken).ConfigureAwait(false);

        try
        {
            return await session.ExecuteAsync(configuration, actions, progress, autoParallelism, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await ResetSessionAsync(session).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException)
        {
            await ResetSessionAsync(session).ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionGate.Wait();
        try
        {
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _sessionGate.Release();
            _sessionGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private async Task<IWorkerSyncSession> GetOrCreateSessionAsync(bool runElevated, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null && (!runElevated || _session.IsElevated))
            {
                return _session;
            }

            _session?.Dispose();
            _session = await _sessionFactory(runElevated, cancellationToken).ConfigureAwait(false);
            return _session;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task ResetSessionAsync(IWorkerSyncSession session)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_session, session))
            {
                _session.Dispose();
                _session = null;
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private static WorkerSyncSessionFactory CreateDefaultSessionFactory(Func<string?> processPathResolver, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(processPathResolver);

        return (runElevated, cancellationToken) => NamedPipeWorkerSyncSession.CreateAsync(processPathResolver, connectTimeout, runElevated, cancellationToken);
    }

    private sealed class NamedPipeWorkerSyncSession : IWorkerSyncSession
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly Process _process;
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SyncWorkerMessageWriter _messageWriter;
        private readonly SemaphoreSlim _executionGate = new(1, 1);
        private bool _disposed;

        private NamedPipeWorkerSyncSession(
            bool isElevated,
            Process process,
            NamedPipeServerStream pipe,
            StreamReader reader,
            StreamWriter writer)
        {
            IsElevated = isElevated;
            _process = process;
            _pipe = pipe;
            _reader = reader;
            _writer = writer;
            _messageWriter = new SyncWorkerMessageWriter(writer);
        }

        public bool IsElevated { get; }

        public static async Task<IWorkerSyncSession> CreateAsync(
            Func<string?> processPathResolver,
            TimeSpan connectTimeout,
            bool runElevated,
            CancellationToken cancellationToken)
        {
            var processPath = processPathResolver();
            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("UsbFileSync could not locate its worker executable.");
            }

            var pipeName = $"UsbFileSync.SyncWorker.{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            try
            {
                var process = StartWorkerProcess(processPath, pipeName, runElevated);
                using var connectCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCancellationTokenSource.CancelAfter(connectTimeout);
                await pipe.WaitForConnectionAsync(connectCancellationTokenSource.Token).ConfigureAwait(false);

                var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
                return new NamedPipeWorkerSyncSession(runElevated, process, pipe, reader, writer);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        public async Task<SyncResult> ExecuteAsync(
            SyncConfiguration configuration,
            IReadOnlyList<SyncAction> actions,
            IProgress<SyncProgress>? progress,
            IProgress<int>? autoParallelism,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    _ = _messageWriter.WriteAsync(new SyncWorkerMessage
                    {
                        Kind = SyncWorkerMessageKinds.Cancel,
                    }, CancellationToken.None);
                });

                await _messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Start,
                    Request = new SyncWorkerRequest(configuration, actions.ToList()),
                }, cancellationToken).ConfigureAwait(false);

                return await ReadWorkerMessagesAsync(_reader, progress, autoParallelism, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _executionGate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Shutdown,
                }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.WaitForExit(2000);
                }

                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                _reader.Dispose();
                _writer.Dispose();
                _pipe.Dispose();
                _process.Dispose();
                _executionGate.Dispose();
            }
        }

        private static Process StartWorkerProcess(string processPath, string pipeName, bool runElevated)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = $"--sync-worker --pipe {pipeName}",
                    UseShellExecute = true,
                };

                if (runElevated)
                {
                    startInfo.Verb = "runas";
                }

                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException("UsbFileSync could not launch the background sync worker.");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("The elevation prompt was cancelled.", exception);
            }
        }

        private static async Task<SyncResult> ReadWorkerMessagesAsync(
            StreamReader reader,
            IProgress<SyncProgress>? progress,
            IProgress<int>? autoParallelism,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var message = await SyncWorkerProtocol.ReadAsync(reader, CancellationToken.None).ConfigureAwait(false);
                if (message is null)
                {
                    throw cancellationToken.IsCancellationRequested
                        ? new OperationCanceledException("Synchronization stopped.", cancellationToken)
                        : new IOException("The background sync worker disconnected unexpectedly.");
                }

                switch (message.Kind)
                {
                    case SyncWorkerMessageKinds.Progress when message.Progress is not null:
                        progress?.Report(message.Progress);
                        break;

                    case SyncWorkerMessageKinds.AutoParallelism when message.EffectiveParallelism.HasValue:
                        autoParallelism?.Report(message.EffectiveParallelism.Value);
                        break;

                    case SyncWorkerMessageKinds.Completed when message.Result is not null:
                        return message.Result;

                    case SyncWorkerMessageKinds.Cancelled:
                        throw new OperationCanceledException("Synchronization stopped.", cancellationToken);

                    case SyncWorkerMessageKinds.Faulted:
                        throw new InvalidOperationException(message.ErrorMessage ?? "The background sync worker failed.");
                }
            }
        }
    }
}