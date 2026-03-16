using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class OneDriveVolumeSourceTests
{
    [Fact]
    public void WritableVolume_ReportsWritableAndDelegatesMutations()
    {
        var client = new FakeOneDriveClient();
        var volume = new OneDriveVolumeSource(client, allowWriteAccess: true);

        Assert.False(volume.IsReadOnly);

        volume.CreateDirectory("Projects/2026");
        volume.DeleteFile("Projects/plan.txt");
        volume.DeleteDirectory("Projects/Empty");
        volume.Move("Projects/old.txt", "Projects/new.txt", overwrite: true);
        volume.SetLastWriteTimeUtc("Projects/new.txt", new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["Projects/2026"], client.CreatedDirectories);
        Assert.Equal(["Projects/plan.txt"], client.DeletedFiles);
        Assert.Equal(["Projects/Empty"], client.DeletedDirectories);
        var move = Assert.Single(client.Moves);
        Assert.Equal("Projects/old.txt", move.SourcePath);
        Assert.Equal("Projects/new.txt", move.DestinationPath);
        Assert.True(move.Overwrite);
        var timestampUpdate = Assert.Single(client.LastWriteTimeUpdates);
        Assert.Equal("Projects/new.txt", timestampUpdate.Path);
    }

    [Fact]
    public void ReadOnlyVolume_RejectsMutations()
    {
        var client = new FakeOneDriveClient();
        var volume = new OneDriveVolumeSource(client, allowWriteAccess: false);

        Assert.True(volume.IsReadOnly);
        Assert.Throws<ReadOnlyVolumeException>(() => volume.CreateDirectory("Projects"));
        Assert.Throws<ReadOnlyVolumeException>(() => volume.OpenWrite("Projects/file.txt"));
    }

    [Fact]
    public void Enumerate_MapsEntriesUnderOneDrivePaths()
    {
        var client = new FakeOneDriveClient
        {
            EnumeratedItems =
            [
                new OneDriveApiClient.OneDriveItem("1", "Docs", true, null, DateTime.UtcNow, true),
                new OneDriveApiClient.OneDriveItem("2", "budget.xlsx", false, 2048, DateTime.UtcNow, true),
            ]
        };
        var volume = new OneDriveVolumeSource(client, allowWriteAccess: false);

        var entries = volume.Enumerate(string.Empty).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Collection(
            entries,
            file =>
            {
                Assert.Equal("budget.xlsx", file.Name);
                Assert.False(file.IsDirectory);
                Assert.Equal("onedrive://root/budget.xlsx", file.FullPath);
            },
            directory =>
            {
                Assert.Equal("Docs", directory.Name);
                Assert.True(directory.IsDirectory);
                Assert.Equal("onedrive://root/Docs", directory.FullPath);
            });
    }

    private sealed class FakeOneDriveClient : IOneDriveClient
    {
        public IReadOnlyList<OneDriveApiClient.OneDriveItem> EnumeratedItems { get; init; } = [];

        public List<string> CreatedDirectories { get; } = [];

        public List<string> DeletedFiles { get; } = [];

        public List<string> DeletedDirectories { get; } = [];

        public List<(string SourcePath, string DestinationPath, bool Overwrite)> Moves { get; } = [];

        public List<(string Path, DateTime LastWriteTimeUtc)> LastWriteTimeUpdates { get; } = [];

        public Task<OneDriveApiClient.OneDriveItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrEmpty(relativePath)
                ? OneDriveApiClient.OneDriveItem.Root
                : OneDriveApiClient.OneDriveItem.NotFound(relativePath));

        public Task<IReadOnlyList<OneDriveApiClient.OneDriveItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(EnumeratedItems);

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task UploadFileAsync(string relativePath, string localFilePath, bool overwrite = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            CreatedDirectories.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            DeletedFiles.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            DeletedDirectories.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourceRelativePath, string destinationRelativePath, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            Moves.Add((sourceRelativePath, destinationRelativePath, overwrite));
            return Task.CompletedTask;
        }

        public Task SetLastWriteTimeUtcAsync(string relativePath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
        {
            LastWriteTimeUpdates.Add((relativePath, lastWriteTimeUtc));
            return Task.CompletedTask;
        }
    }
}