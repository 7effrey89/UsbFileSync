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
        Assert.Equal(SyncActionType.OverwriteFileOnSource, action.Type);
        Assert.Equal("shared.txt", action.RelativePath);
    }

    [Fact]
    public async Task AnalyzeChangesAsync_OneWayUsesOverwriteActionForChangedDestinationFile()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "shared.txt", "new content", new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "shared.txt", "old content", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.OverwriteFileOnDestination, action.Type);
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
        Assert.Equal(3, result.AppliedOperations);
        Assert.True(File.Exists(Path.Combine(destination, "folder", "data.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "stale.txt")));
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(destination, "folder", "data.txt")));
    }

    [Fact]
    public async Task AnalyzeChangesAsync_OneWayDetectsEmptyDirectoryCreation()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        Directory.CreateDirectory(Path.Combine(source, "EmptyFolder"));

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.CreateDirectoryOnDestination, action.Type);
        Assert.Equal("EmptyFolder", action.RelativePath);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesEmptyDirectoryOnDestination()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        Directory.CreateDirectory(Path.Combine(source, "EmptyFolder"));

        var service = new SyncService();
        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        Assert.False(result.IsDryRun);
        Assert.Equal(1, result.AppliedOperations);
        Assert.True(Directory.Exists(Path.Combine(destination, "EmptyFolder")));
    }

    [Fact]
    public async Task AnalyzeChangesAsync_OneWaySkipsWindowsSystemMetadataFolders()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "user.txt", "payload", new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(source, Path.Combine("System Volume Information", "IndexerVolumeGuid"), "metadata", new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.CopyToDestination, action.Type);
        Assert.Equal("user.txt", action.RelativePath);
    }

    [Fact]
    public async Task BuildPreviewAsync_GroupsNewChangedDeletedAndUnchangedItems()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "new.txt", "new", new DateTime(2024, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(source, "shared.txt", "new content", new DateTime(2024, 5, 4, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(source, "same.txt", "same", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "shared.txt", "old content", new DateTime(2024, 5, 2, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "same.txt", "same", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "deleted.txt", "remove me", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var preview = await service.BuildPreviewAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        Assert.Contains(preview, item => item.RelativePath == "new.txt" && item.Category == SyncPreviewCategory.NewFiles && item.Status == "New File");
        Assert.Contains(preview, item => item.RelativePath == "shared.txt" && item.Category == SyncPreviewCategory.ChangedFiles && item.Status == "Modified");
        Assert.Contains(preview, item => item.RelativePath == "deleted.txt" && item.Category == SyncPreviewCategory.DeletedFiles && item.Status == "Deleted");
        Assert.Contains(preview, item => item.RelativePath == "same.txt" && item.Category == SyncPreviewCategory.UnchangedFiles && item.Status == "Unchanged");
    }

    [Fact]
    public async Task ExecutePlannedAsync_UsesProvidedActionOrder()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "queued.txt", "payload", new DateTime(2024, 5, 5, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var plannedActions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        var result = await service.ExecutePlannedAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        }, plannedActions);

        Assert.Single(result.Actions);
        Assert.Equal("queued.txt", result.Actions[0].RelativePath);
        Assert.True(File.Exists(Path.Combine(destination, "queued.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_CopiesFiles_WhenParallelCopyCountIsGreaterThanOne()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "a.txt", new string('a', 2048), new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(source, "b.txt", new string('b', 2048), new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            ParallelCopyCount = 4,
        });

        Assert.Equal(2, result.AppliedOperations);
        Assert.True(File.Exists(Path.Combine(destination, "a.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "b.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_CopiesFiles_WhenParallelCopyCountIsUnlimited()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "a.txt", new string('a', 2048), new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(source, "b.txt", new string('b', 2048), new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            ParallelCopyCount = 0,
        });

        Assert.Equal(2, result.AppliedOperations);
        Assert.True(File.Exists(Path.Combine(destination, "a.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "b.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_CopiesFiles_WhenChecksumValidationIsEnabled()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "verified.bin", new string('v', 4096), new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var result = await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            VerifyChecksums = true,
        });

        Assert.Equal(1, result.AppliedOperations);
        Assert.True(File.Exists(Path.Combine(destination, "verified.bin")));
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(source, "verified.bin")), await File.ReadAllTextAsync(Path.Combine(destination, "verified.bin")));
    }

    [Fact]
    public async Task ExecutePlannedAsync_CancelledNewCopy_DoesNotLeavePartialDestinationFile()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "large.bin", new string('x', 4 * 1024 * 1024), new DateTime(2024, 5, 7, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = new[]
        {
            new SyncAction(
                SyncActionType.CopyToDestination,
                "large.bin",
                Path.Combine(source, "large.bin"),
                Path.Combine(destination, "large.bin")),
        };

        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<SyncProgress>(update =>
        {
            if (update.CurrentItemBytesTransferred > 0)
            {
                cancellationTokenSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecutePlannedAsync(
            new SyncConfiguration
            {
                SourcePath = source,
                DestinationPath = destination,
                Mode = SyncMode.OneWay,
            },
            actions,
            progress,
            cancellationTokenSource.Token));

        Assert.False(File.Exists(Path.Combine(destination, "large.bin")));
        Assert.Empty(Directory.GetFiles(destination, "*.usfcopy.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExecutePlannedAsync_CancelledOverwrite_PreservesExistingDestinationFile()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "large.bin", new string('n', 4 * 1024 * 1024), new DateTime(2024, 5, 8, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "large.bin", "original destination content", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var actions = new[]
        {
            new SyncAction(
                SyncActionType.OverwriteFileOnDestination,
                "large.bin",
                Path.Combine(source, "large.bin"),
                Path.Combine(destination, "large.bin")),
        };

        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<SyncProgress>(update =>
        {
            if (update.CurrentItemBytesTransferred > 0)
            {
                cancellationTokenSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecutePlannedAsync(
            new SyncConfiguration
            {
                SourcePath = source,
                DestinationPath = destination,
                Mode = SyncMode.OneWay,
            },
            actions,
            progress,
            cancellationTokenSource.Token));

        Assert.True(File.Exists(Path.Combine(destination, "large.bin")));
        Assert.Equal("original destination content", await File.ReadAllTextAsync(Path.Combine(destination, "large.bin")));
        Assert.Empty(Directory.GetFiles(destination, "*.usfcopy.tmp", SearchOption.TopDirectoryOnly));
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
