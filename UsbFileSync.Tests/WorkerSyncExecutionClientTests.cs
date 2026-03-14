using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class WorkerSyncExecutionClientTests
{
    [Fact]
    public async Task ExecuteAsync_ReusesElevatedSessionAcrossCalls()
    {
        var volumeService = new StubSourceVolumeService(new StubVolumeSource(isReadOnly: true, fileSystemType: "ext4"));
        var createdSessions = new List<FakeWorkerSyncSession>();
        using var client = new WorkerSyncExecutionClient(
            volumeService,
            (runElevated, _) =>
            {
                var session = new FakeWorkerSyncSession(runElevated);
                createdSessions.Add(session);
                return Task.FromResult<IWorkerSyncSession>(session);
            });

        var configuration = CreateConfiguration("D:\\");
        var actions = CreateActions();

        await client.ExecuteAsync(configuration, actions, progress: null, autoParallelism: null, CancellationToken.None);
        await client.ExecuteAsync(configuration, actions, progress: null, autoParallelism: null, CancellationToken.None);

        var session = Assert.Single(createdSessions);
        Assert.True(session.IsElevated);
        Assert.Equal(2, session.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReplacesNonElevatedSession_WhenLaterSyncNeedsElevation()
    {
        var volumeService = new ConditionalSourceVolumeService();
        var createdSessions = new List<FakeWorkerSyncSession>();
        using var client = new WorkerSyncExecutionClient(
            volumeService,
            (runElevated, _) =>
            {
                var session = new FakeWorkerSyncSession(runElevated);
                createdSessions.Add(session);
                return Task.FromResult<IWorkerSyncSession>(session);
            });

        await client.ExecuteAsync(CreateConfiguration("C:\\dest"), CreateActions(), progress: null, autoParallelism: null, CancellationToken.None);
        await client.ExecuteAsync(CreateConfiguration("D:\\"), CreateActions(), progress: null, autoParallelism: null, CancellationToken.None);

        Assert.Equal(2, createdSessions.Count);
        Assert.False(createdSessions[0].IsElevated);
        Assert.True(createdSessions[0].Disposed);
        Assert.True(createdSessions[1].IsElevated);
        Assert.False(createdSessions[1].Disposed);
    }

    [Fact]
    public async Task Dispose_DisposesActiveSession()
    {
        var volumeService = new StubSourceVolumeService(new StubVolumeSource(isReadOnly: true, fileSystemType: "ext4"));
        FakeWorkerSyncSession? session = null;
        var client = new WorkerSyncExecutionClient(
            volumeService,
            (runElevated, _) =>
            {
                session = new FakeWorkerSyncSession(runElevated);
                return Task.FromResult<IWorkerSyncSession>(session);
            });

        await client.ExecuteAsync(CreateConfiguration("D:\\"), CreateActions(), progress: null, autoParallelism: null, CancellationToken.None);
        client.Dispose();

        Assert.NotNull(session);
        Assert.True(session!.Disposed);
    }

    private static SyncConfiguration CreateConfiguration(string destinationPath) => new()
    {
        SourcePath = "C:\\source",
        DestinationPath = destinationPath,
        DestinationPaths = [destinationPath],
        Mode = SyncMode.OneWay,
    };

    private static IReadOnlyList<SyncAction> CreateActions() =>
        [new SyncAction(SyncActionType.CopyToDestination, "file.txt", @"C:\source\file.txt", @"D:\file.txt")];

    private sealed class FakeWorkerSyncSession(bool isElevated) : IWorkerSyncSession
    {
        public bool IsElevated { get; } = isElevated;

        public int ExecuteCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task<SyncResult> ExecuteAsync(
            SyncConfiguration configuration,
            IReadOnlyList<SyncAction> actions,
            IProgress<SyncProgress>? progress,
            IProgress<int>? autoParallelism,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(new SyncResult(actions.ToList(), actions.Count, false));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class StubSourceVolumeService(IVolumeSource? volume) : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out IVolumeSource? resolvedVolume, out string? failureReason)
        {
            resolvedVolume = volume;
            failureReason = null;
            return volume is not null;
        }
    }

    private sealed class ConditionalSourceVolumeService : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out IVolumeSource? resolvedVolume, out string? failureReason)
        {
            if (string.Equals(path, "D:\\", StringComparison.OrdinalIgnoreCase))
            {
                resolvedVolume = new StubVolumeSource(isReadOnly: true, fileSystemType: "ext4");
                failureReason = null;
                return true;
            }

            resolvedVolume = null;
            failureReason = null;
            return false;
        }
    }

    private sealed class StubVolumeSource(bool isReadOnly, string fileSystemType) : IVolumeSource
    {
        public string Id => "stub";

        public string DisplayName => "stub";

        public string FileSystemType => fileSystemType;

        public bool IsReadOnly => isReadOnly;

        public string Root => @"D:\";

        public IFileEntry GetEntry(string path) => throw new NotSupportedException();

        public IEnumerable<IFileEntry> Enumerate(string path) => throw new NotSupportedException();

        public Stream OpenRead(string path) => throw new NotSupportedException();

        public Stream OpenWrite(string path, bool overwrite = true) => throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();

        public void DeleteDirectory(string path) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new NotSupportedException();

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new NotSupportedException();

        private sealed class StubFileEntry : IFileEntry
        {
            public string FullPath => string.Empty;

            public string Name => string.Empty;

            public bool IsDirectory => false;

            public long? Size => null;

            public DateTime? LastWriteTimeUtc => null;

            public bool Exists => false;
        }
    }
}