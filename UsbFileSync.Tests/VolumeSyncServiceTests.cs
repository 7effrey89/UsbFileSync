using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class VolumeSyncServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.VolumeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzeChangesAsync_UsesConfiguredVolumeSources()
    {
        var sourceBackingPath = CreateDirectory("ext-source");
        var destinationBackingPath = CreateDirectory("ext-destination");
        WriteFile(sourceBackingPath, "folder/new.txt", "payload", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var sourceVolume = new ExtVolumeSource("linux-source", "Linux Source", sourceBackingPath);
        var destinationVolume = new ExtVolumeSource("linux-destination", "Linux Destination", destinationBackingPath);
        var service = new SyncService();

        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourceVolume = sourceVolume,
            DestinationVolume = destinationVolume,
            DestinationVolumes = [destinationVolume],
            Mode = SyncMode.OneWay,
        });

        Assert.Collection(actions,
            action =>
            {
                Assert.Equal(SyncActionType.CreateDirectoryOnDestination, action.Type);
                Assert.Equal("folder", action.RelativePath);
                Assert.Equal("ext4://linux-destination/folder", action.DestinationFullPath);
            },
            action =>
            {
                Assert.Equal(SyncActionType.CopyToDestination, action.Type);
                Assert.Equal("folder/new.txt", action.RelativePath);
                Assert.Equal("ext4://linux-source/folder/new.txt", action.SourceFullPath);
                Assert.Equal("ext4://linux-destination/folder/new.txt", action.DestinationFullPath);
            });
    }

    [Fact]
    public async Task ExecuteAsync_CopiesFromReadOnlyApfsSourceWithoutWritingMetadataToSource()
    {
        var sourceBackingPath = CreateDirectory("apfs-source");
        var destinationBackingPath = CreateDirectory("windows-destination");
        WriteFile(sourceBackingPath, "album/song.txt", "music", new DateTime(2024, 5, 2, 0, 0, 0, DateTimeKind.Utc));

        var sourceVolume = new ApfsVolumeSource("mac-usb", "APFS USB", sourceBackingPath);
        var destinationVolume = new WindowsMountedVolume(destinationBackingPath);
        var service = new SyncService();

        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourceVolume = sourceVolume,
            DestinationPath = destinationBackingPath,
            DestinationVolume = destinationVolume,
            DestinationVolumes = [destinationVolume],
            Mode = SyncMode.OneWay,
        });

        Assert.False(result.IsDryRun);
        Assert.Equal("music", await File.ReadAllTextAsync(Path.Combine(destinationBackingPath, "album", "song.txt")));
        Assert.False(Directory.Exists(Path.Combine(sourceBackingPath, ".sync-metadata")));
        Assert.True(Directory.Exists(Path.Combine(destinationBackingPath, ".sync-metadata")));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsReadOnlyApfsDestination()
    {
        var sourceBackingPath = CreateDirectory("windows-source");
        var destinationBackingPath = CreateDirectory("apfs-destination");
        WriteFile(sourceBackingPath, "photo.jpg", "image", new DateTime(2024, 5, 3, 0, 0, 0, DateTimeKind.Utc));

        var sourceVolume = new WindowsMountedVolume(sourceBackingPath);
        var destinationVolume = new ApfsVolumeSource("mac-destination", "APFS Backup", destinationBackingPath);
        var service = new SyncService();

        await Assert.ThrowsAsync<ReadOnlyVolumeException>(() => service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = sourceBackingPath,
            SourceVolume = sourceVolume,
            DestinationVolume = destinationVolume,
            DestinationVolumes = [destinationVolume],
            Mode = SyncMode.OneWay,
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string rootPath, string relativePath, string contents, DateTime lastWriteTimeUtc)
    {
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
        File.SetLastWriteTimeUtc(fullPath, lastWriteTimeUtc);
    }
}
