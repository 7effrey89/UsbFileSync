using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    public async Task AnalyzeChangesAsync_OneWayIgnoresEquivalentTwoSecondTimestampDrift()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        var sourceTimestamp = new DateTime(2024, 7, 26, 15, 16, 40, DateTimeKind.Utc);
        var destinationTimestamp = sourceTimestamp.AddSeconds(-2);
        WriteFile(source, "shared.txt", "same content", sourceTimestamp);
        WriteFile(destination, "shared.txt", "same content", destinationTimestamp);

        var service = new SyncService();
        var actions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        });

        Assert.Empty(actions);
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
        WriteFile(source, Path.Combine(".sync-metadata", "file-index.json"), "{}", new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc));
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
    public async Task ExecuteAsync_TwoWayPersistsMetadataAndPropagatesTrackedDeletion()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "shared.txt", "same", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "shared.txt", "same", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var configuration = new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.TwoWay,
        };

        var initialResult = await service.ExecuteAsync(configuration);

        Assert.Equal(0, initialResult.AppliedOperations);

        using var sourceMetadata = await LoadMetadataAsync(source);
        using var destinationMetadata = await LoadMetadataAsync(destination);
        var sourceRootId = sourceMetadata.RootElement.GetProperty("RootId").GetString();
        var destinationRootId = destinationMetadata.RootElement.GetProperty("RootId").GetString();
        Assert.Equal(source, sourceMetadata.RootElement.GetProperty("RootName").GetString());
        Assert.Equal(destination, destinationMetadata.RootElement.GetProperty("RootName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sourceRootId));
        Assert.False(string.IsNullOrWhiteSpace(destinationRootId));

        var synchronizedEntry = sourceMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(destinationRootId!)
            .GetProperty("Entries")
            .GetProperty("shared.txt");
        Assert.Equal(destination, sourceMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(destinationRootId!)
            .GetProperty("PeerRootName")
            .GetString());
        Assert.Equal(source, destinationMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(sourceRootId!)
            .GetProperty("PeerRootName")
            .GetString());
        Assert.False(synchronizedEntry.GetProperty("IsDeleted").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(synchronizedEntry.GetProperty("LastSyncedByRootId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(synchronizedEntry.GetProperty("LastSyncedByRootName").GetString()));

        File.Delete(Path.Combine(destination, "shared.txt"));

        var actions = await service.AnalyzeChangesAsync(configuration);

        var action = Assert.Single(actions);
        Assert.Equal(SyncActionType.DeleteFromSource, action.Type);

        await service.ExecutePlannedAsync(configuration, actions);

        Assert.False(File.Exists(Path.Combine(source, "shared.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "shared.txt")));

        using var updatedSourceMetadata = await LoadMetadataAsync(source);
        var deletedEntry = updatedSourceMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(destinationRootId!)
            .GetProperty("Entries")
            .GetProperty("shared.txt");

        Assert.True(deletedEntry.GetProperty("IsDeleted").GetBoolean());
        Assert.Equal(destinationRootId, deletedEntry.GetProperty("LastSyncedByRootId").GetString());
        Assert.Equal(destination, deletedEntry.GetProperty("LastSyncedByRootName").GetString());
    }

    [Fact]
    public async Task AnalyzeChangesAsync_TwoWayIgnoresEquivalentTwoSecondTimestampDriftAfterMetadataBaseline()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        var timestamp = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteFile(source, "shared.txt", "same", timestamp);
        WriteFile(destination, "shared.txt", "same", timestamp);

        var service = new SyncService();
        var configuration = new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.TwoWay,
        };

        await service.ExecuteAsync(configuration);

        File.SetLastWriteTimeUtc(Path.Combine(destination, "shared.txt"), timestamp.AddSeconds(-2));

        var actions = await service.AnalyzeChangesAsync(configuration);

        Assert.Empty(actions);
    }

    [Fact]
    public async Task ExecuteAsync_OneWayPersistsMetadataThatTwoWayCanReuse()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "shared.txt", "same", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();

        var oneWayConfiguration = new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
        };

        var result = await service.ExecuteAsync(oneWayConfiguration);

        Assert.False(result.IsDryRun);
        Assert.Equal(1, result.AppliedOperations);

        using var sourceMetadata = await LoadMetadataAsync(source);
        using var destinationMetadata = await LoadMetadataAsync(destination);
        var sourceRootId = sourceMetadata.RootElement.GetProperty("RootId").GetString();
        var destinationRootId = destinationMetadata.RootElement.GetProperty("RootId").GetString();
        Assert.Equal(source, sourceMetadata.RootElement.GetProperty("RootName").GetString());
        Assert.Equal(destination, destinationMetadata.RootElement.GetProperty("RootName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sourceRootId));
        Assert.False(string.IsNullOrWhiteSpace(destinationRootId));
        Assert.True(sourceMetadata.RootElement
            .GetProperty("PeerStates")
            .TryGetProperty(destinationRootId!, out var peerState));
        Assert.Equal(destination, peerState.GetProperty("PeerRootName").GetString());
        Assert.True(peerState.GetProperty("Entries").TryGetProperty("shared.txt", out var synchronizedEntry));
        Assert.False(synchronizedEntry.GetProperty("IsDeleted").GetBoolean());
        Assert.Equal(source, synchronizedEntry.GetProperty("LastSyncedByRootName").GetString());

        File.Delete(Path.Combine(destination, "shared.txt"));

        var twoWayActions = await service.AnalyzeChangesAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.TwoWay,
        });

        var action = Assert.Single(twoWayActions);
        Assert.Equal(SyncActionType.DeleteFromSource, action.Type);
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
    public async Task ExecuteAsync_WhenChecksumValidationIsEnabled_PersistsChecksumInMetadata()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        const string content = "verified content";
        WriteFile(source, "verified.bin", content, new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            VerifyChecksums = true,
        });

        using var sourceMetadata = await LoadMetadataAsync(source);
        using var destinationMetadata = await LoadMetadataAsync(destination);
        var destinationRootId = destinationMetadata.RootElement.GetProperty("RootId").GetString();
        var entry = sourceMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(destinationRootId!)
            .GetProperty("Entries")
            .GetProperty("verified.bin");

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
        Assert.Equal(expectedChecksum, entry.GetProperty("ChecksumSha256").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileChangesWithoutChecksumValidation_ClearsStoredChecksum()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "verified.bin", "first", new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            VerifyChecksums = true,
        });

        WriteFile(source, "verified.bin", "second", new DateTime(2024, 5, 7, 0, 0, 0, DateTimeKind.Utc));

        await service.ExecuteAsync(new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            VerifyChecksums = false,
        });

        using var sourceMetadata = await LoadMetadataAsync(source);
        using var destinationMetadata = await LoadMetadataAsync(destination);
        var destinationRootId = destinationMetadata.RootElement.GetProperty("RootId").GetString();
        var entry = sourceMetadata.RootElement
            .GetProperty("PeerStates")
            .GetProperty(destinationRootId!)
            .GetProperty("Entries")
            .GetProperty("verified.bin");

        Assert.False(entry.TryGetProperty("ChecksumSha256", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoredChecksumIsReusable_UsesItForLaterChecksumValidation()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "verified.bin", "verified content", new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var configuration = new SyncConfiguration
        {
            SourcePath = source,
            DestinationPath = destination,
            Mode = SyncMode.OneWay,
            VerifyChecksums = true,
        };

        await service.ExecuteAsync(configuration);

        using var sourceMetadata = await LoadMetadataAsync(source);
        using var destinationMetadata = await LoadMetadataAsync(destination);
        var sourceRootId = sourceMetadata.RootElement.GetProperty("RootId").GetString()!;
        var destinationRootId = destinationMetadata.RootElement.GetProperty("RootId").GetString()!;

        await SetStoredChecksumAsync(source, destinationRootId, "verified.bin", new string('0', 64));
        await SetStoredChecksumAsync(destination, sourceRootId, "verified.bin", new string('0', 64));

        WriteFile(destination, "verified.bin", "stale destination content", new DateTime(2024, 5, 5, 0, 0, 0, DateTimeKind.Utc));

        var exception = await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(configuration));
        Assert.Contains("Checksum validation failed", exception.Message);
    }

    [Fact]
    public async Task ExecutePlannedAsync_OverwriteReplacesExistingDestinationFile()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        WriteFile(source, "shared.txt", "new content", new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(destination, "shared.txt", "old content", new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = new SyncService();
        var result = await service.ExecutePlannedAsync(
            new SyncConfiguration
            {
                SourcePath = source,
                DestinationPath = destination,
                Mode = SyncMode.OneWay,
            },
            [
                new SyncAction(
                    SyncActionType.OverwriteFileOnDestination,
                    "shared.txt",
                    Path.Combine(source, "shared.txt"),
                    Path.Combine(destination, "shared.txt")),
            ]);

        Assert.False(result.IsDryRun);
        Assert.Equal(1, result.AppliedOperations);
        Assert.Equal("new content", await File.ReadAllTextAsync(Path.Combine(destination, "shared.txt")));
    }

    [Fact]
    public async Task ExecutePlannedAsync_CopyPreservesSourceTimestampWithinFilesystemResolution()
    {
        var source = CreateDirectory("source");
        var destination = CreateDirectory("destination");
        var sourceTimestamp = new DateTime(2024, 7, 26, 15, 16, 40, DateTimeKind.Utc);
        WriteFile(source, "shared.txt", "same content", sourceTimestamp);

        var service = new SyncService();
        await service.ExecutePlannedAsync(
            new SyncConfiguration
            {
                SourcePath = source,
                DestinationPath = destination,
                Mode = SyncMode.OneWay,
            },
            [
                new SyncAction(
                    SyncActionType.CopyToDestination,
                    "shared.txt",
                    Path.Combine(source, "shared.txt"),
                    Path.Combine(destination, "shared.txt")),
            ]);

        var destinationTimestamp = File.GetLastWriteTimeUtc(Path.Combine(destination, "shared.txt"));
        var tolerance = TimeSpan.FromSeconds(2);
        Assert.True(
            (sourceTimestamp - destinationTimestamp).Duration() <= tolerance,
            $"Expected destination timestamp {destinationTimestamp:o} to be within {tolerance} of source timestamp {sourceTimestamp:o}.");
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

    private static async Task<JsonDocument> LoadMetadataAsync(string root)
    {
        var metadataPath = Path.Combine(root, ".sync-metadata", "file-index.json");
        return JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
    }

    private static async Task SetStoredChecksumAsync(string root, string peerRootId, string relativePath, string checksum)
    {
        var metadataPath = Path.Combine(root, ".sync-metadata", "file-index.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!.AsObject();
        document["PeerStates"]![peerRootId]!["Entries"]![relativePath]!["ChecksumSha256"] = checksum;
        await File.WriteAllTextAsync(metadataPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
