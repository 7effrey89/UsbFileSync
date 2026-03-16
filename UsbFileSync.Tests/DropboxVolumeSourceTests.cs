using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class DropboxVolumeSourceTests
{
    [Fact]
    public void WritableVolume_ReportsWritableAndDelegatesMutations()
    {
        var client = new FakeDropboxClient();
        var volume = new DropboxVolumeSource(client, registrationId: "dropbox-account", displayName: "Dropbox - Team", allowWriteAccess: true);

        Assert.False(volume.IsReadOnly);
        Assert.Equal("Dropbox - Team", volume.DisplayName);
        Assert.Equal("dropbox://account/dropbox-account", volume.Root);

        volume.CreateDirectory("Albums/2026");
        volume.DeleteFile("Albums/song.txt");
        volume.DeleteDirectory("Albums/Empty");
        volume.Move("Albums/old.txt", "Albums/new.txt", overwrite: true);
        volume.SetLastWriteTimeUtc("Albums/new.txt", new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc));

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
        var client = new FakeDropboxClient();
        var volume = new DropboxVolumeSource(client, registrationId: "dropbox-account", displayName: "Dropbox - Team", allowWriteAccess: false);

        Assert.True(volume.IsReadOnly);
        Assert.Throws<ReadOnlyVolumeException>(() => volume.CreateDirectory("Albums"));
        Assert.Throws<ReadOnlyVolumeException>(() => volume.OpenWrite("Albums/song.txt"));
    }

    private sealed class FakeDropboxClient : IDropboxClient
    {
        public IReadOnlyList<DropboxApiClient.DropboxItem> EnumeratedItems { get; init; } = [];

        public List<string> CreatedDirectories { get; } = [];

        public List<string> DeletedFiles { get; } = [];

        public List<string> DeletedDirectories { get; } = [];

        public List<(string SourcePath, string DestinationPath, bool Overwrite)> Moves { get; } = [];

        public List<(string Path, DateTime LastWriteTimeUtc)> LastWriteTimeUpdates { get; } = [];

        public Task<DropboxApiClient.DropboxItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrEmpty(relativePath)
                ? DropboxApiClient.DropboxItem.Root
                : DropboxApiClient.DropboxItem.NotFound(relativePath));

        public Task<IReadOnlyList<DropboxApiClient.DropboxItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default) =>
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