using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class GoogleDriveVolumeSourceTests
{
    [Fact]
    public void WritableVolume_ReportsWritableAndDelegatesMutations()
    {
        var client = new FakeGoogleDriveClient();
        var volume = new GoogleDriveVolumeSource(client, allowWriteAccess: true);

        Assert.False(volume.IsReadOnly);

        volume.CreateDirectory("Albums/2026");
        volume.DeleteFile("Albums/song.txt");
        volume.DeleteDirectory("Albums/Empty");
        volume.Move("Albums/old.txt", "Albums/new.txt", overwrite: true);
        volume.SetLastWriteTimeUtc("Albums/new.txt", new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["Albums/2026"], client.CreatedDirectories);
        Assert.Equal(["Albums/song.txt"], client.DeletedFiles);
        Assert.Equal(["Albums/Empty"], client.DeletedDirectories);
        var move = Assert.Single(client.Moves);
        Assert.Equal("Albums/old.txt", move.SourcePath);
        Assert.Equal("Albums/new.txt", move.DestinationPath);
        Assert.True(move.Overwrite);
        var timestampUpdate = Assert.Single(client.LastWriteTimeUpdates);
        Assert.Equal("Albums/new.txt", timestampUpdate.Path);
    }

    [Fact]
    public void ReadOnlyVolume_RejectsMutations()
    {
        var client = new FakeGoogleDriveClient();
        var volume = new GoogleDriveVolumeSource(client, allowWriteAccess: false);

        Assert.True(volume.IsReadOnly);
        Assert.Throws<ReadOnlyVolumeException>(() => volume.CreateDirectory("Albums"));
        Assert.Throws<ReadOnlyVolumeException>(() => volume.OpenWrite("Albums/song.txt"));
    }

    [Fact]
    public void Enumerate_ThrowsHelpfulError_WhenDriveFolderContainsDuplicateSiblingNames()
    {
        var client = new FakeGoogleDriveClient
        {
            EnumeratedItems =
            [
                new GoogleDriveApiClient.GoogleDriveItem("1", "PNG image.png", false, 100, DateTime.UtcNow, true),
                new GoogleDriveApiClient.GoogleDriveItem("2", "PNG image.png", false, 200, DateTime.UtcNow, true),
            ]
        };
        var volume = new GoogleDriveVolumeSource(client, allowWriteAccess: false);

        var exception = Assert.Throws<InvalidOperationException>(() => volume.Enumerate(string.Empty).ToArray());

        Assert.Contains("duplicate sibling names", exception.Message);
        Assert.Contains("PNG image.png", exception.Message);
    }

    private sealed class FakeGoogleDriveClient : IGoogleDriveClient
    {
        public IReadOnlyList<GoogleDriveApiClient.GoogleDriveItem> EnumeratedItems { get; init; } = [];

        public List<string> CreatedDirectories { get; } = [];

        public List<string> DeletedFiles { get; } = [];

        public List<string> DeletedDirectories { get; } = [];

        public List<(string SourcePath, string DestinationPath, bool Overwrite)> Moves { get; } = [];

        public List<(string Path, DateTime LastWriteTimeUtc)> LastWriteTimeUpdates { get; } = [];

        public Task<GoogleDriveApiClient.GoogleDriveItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrEmpty(relativePath)
                ? GoogleDriveApiClient.GoogleDriveItem.Root
                : GoogleDriveApiClient.GoogleDriveItem.NotFound(relativePath));

        public Task<IReadOnlyList<GoogleDriveApiClient.GoogleDriveItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default) =>
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