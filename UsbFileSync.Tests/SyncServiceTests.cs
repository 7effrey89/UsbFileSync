using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Tests;

public sealed class SyncServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzeChangesAsync_OneWayDetectsCopiesAndDeletes()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "new.txt", "source data", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "obsolete.txt", "old data", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        Assert.Collection(actions,
            action =>
            {
                Assert.Equal(SyncActionType.CopyToDestination, action.Type);
                Assert.Equal("new.txt", action.RelativePath);
            },
            action =>
            {
                Assert.Equal(SyncActionType.DeleteFromDestination, action.Type);
                Assert.Equal("obsolete.txt", action.RelativePath);
            });
    }

    [Fact]
    public async Task AnalyzeChangesAsync_OneWayTurnsRenameIntoDestinationMove()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        var timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteFile(source, "renamed.txt", "same content", timestamp);
        WriteFile(destination, "original.txt", "same content", timestamp);

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            DetectMoves = true,
        });

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.MoveOnDestination, action.Type);
        Assert.Equal("renamed.txt", action.RelativePath);
        Assert.Equal("original.txt", action.PreviousRelativePath);
    }

    [Fact]
    public async Task AnalyzeChangesAsync_TwoWayCopiesNewerFileBackToSource()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "shared.txt", "older", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "shared.txt", "newer", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.TwoWay,
        });

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.CopyToSource, action.Type);
        Assert.Equal("shared.txt", action.RelativePath);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesPlannedOperations()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        var timestamp = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteFile(source, "folder/data.txt", "payload", timestamp);
        WriteFile(destination, "stale.txt", "remove", timestamp);

        var service = new SyncService();
        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        Assert.False(result.IsDryRun);
        Assert.Equal(2, result.AppliedOperations);
        Assert.True(File.Exists(Path.Combine(destination, "folder", "data.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "stale.txt")));
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(destination, "folder", "data.txt")));
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_rootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string relativePath, string content, DateTime timestamp)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, timestamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
