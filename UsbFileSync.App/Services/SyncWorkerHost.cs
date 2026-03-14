using System.IO.Pipes;
using System.Text;
using UsbFileSync.Contracts;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App.Services;

internal static class SyncWorkerHost
{
    private const string WorkerModeArgument = "--sync-worker";
    private const string PipeArgument = "--pipe";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static bool TryGetPipeName(IReadOnlyList<string> args, out string pipeName)
    {
        pipeName = string.Empty;

        if (args.Count < 3 || !string.Equals(args[0], WorkerModeArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(args[1], PipeArgument, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[2]))
        {
            return false;
        }

        pipeName = args[2];
        return true;
    }

    public static async Task<int> RunAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30000, cancellationToken).ConfigureAwait(false);

        using var reader = new System.IO.StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new System.IO.StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
        var messageWriter = new SyncWorkerMessageWriter(writer);

        SyncWorkerMessage? pendingCommand = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var command = pendingCommand ?? await SyncWorkerProtocol.ReadAsync(reader, cancellationToken).ConfigureAwait(false);
            pendingCommand = null;
            if (command is null || string.Equals(command.Kind, SyncWorkerMessageKinds.Shutdown, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (command.Request is null || !string.Equals(command.Kind, SyncWorkerMessageKinds.Start, StringComparison.OrdinalIgnoreCase))
            {
                await messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Faulted,
                    ErrorMessage = "The sync worker did not receive a valid start request.",
                }, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var executionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var commandListenerTask = ListenForCommandsAsync(reader, executionCancellationTokenSource);

            try
            {
                var configuration = SyncVolumeServiceFactory.ResolveConfiguration(command.Request.Configuration);
                var syncService = new SyncService();
                var progress = new Progress<SyncProgress>(update =>
                {
                    messageWriter.WriteAsync(new SyncWorkerMessage
                    {
                        Kind = SyncWorkerMessageKinds.Progress,
                        Progress = update,
                    }).GetAwaiter().GetResult();
                });
                var autoParallelism = new Progress<int>(count =>
                {
                    messageWriter.WriteAsync(new SyncWorkerMessage
                    {
                        Kind = SyncWorkerMessageKinds.AutoParallelism,
                        EffectiveParallelism = count,
                    }).GetAwaiter().GetResult();
                });

                var result = await syncService.ExecutePlannedAsync(
                    configuration,
                    command.Request.Actions,
                    progress,
                    autoParallelism,
                    executionCancellationTokenSource.Token).ConfigureAwait(false);

                await messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Completed,
                    Result = result,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (executionCancellationTokenSource.IsCancellationRequested)
            {
                await messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Cancelled,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await messageWriter.WriteAsync(new SyncWorkerMessage
                {
                    Kind = SyncWorkerMessageKinds.Faulted,
                    ErrorMessage = exception.Message,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                executionCancellationTokenSource.Cancel();
                pendingCommand = await commandListenerTask.ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<SyncWorkerMessage?> ListenForCommandsAsync(System.IO.StreamReader reader, CancellationTokenSource executionCancellationTokenSource)
    {
        try
        {
            while (!executionCancellationTokenSource.IsCancellationRequested)
            {
                var message = await SyncWorkerProtocol.ReadAsync(reader, executionCancellationTokenSource.Token).ConfigureAwait(false);
                if (message is null)
                {
                    executionCancellationTokenSource.Cancel();
                    return null;
                }

                if (string.Equals(message.Kind, SyncWorkerMessageKinds.Cancel, StringComparison.OrdinalIgnoreCase))
                {
                    executionCancellationTokenSource.Cancel();
                    return null;
                }

                return message;
            }
        }
        catch (OperationCanceledException) when (executionCancellationTokenSource.IsCancellationRequested)
        {
        }

        return null;
    }
}