using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;
using System.Collections.Concurrent;

namespace UsbFileSync.Tests;

public sealed class DuplicateFileAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_UsesChecksumToFilterSameSizeNonDuplicates()
    {
        using var workspace = new DuplicateWorkspace();
        workspace.WriteFile("keep.txt", "duplicate-bytes");
        workspace.WriteFile("copy.txt", "duplicate-bytes");
        workspace.WriteFile("same-size-different.txt", "different-bytes");
        var service = new DuplicateFileAnalysisService();

        var result = await service.AnalyzeAsync(
            new WindowsMountedVolume(workspace.RootPath),
            hideMacOsSystemFiles: true,
            excludedPathPatterns: null,
            includeSubfolders: true);

        var group = Assert.Single(result.Groups);
        var pairedPaths = group.Files
            .Select(file => file.RelativePath.Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();
        Assert.Equal(1, result.DuplicateGroupCount);
        Assert.Equal(new[] { "copy.txt", "keep.txt" }, pairedPaths);
        Assert.Equal(group.ChecksumSha256, group.Files[0].ChecksumSha256);
        Assert.True(result.HashedFileCount >= 3);
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsScanningAndHashingProgress()
    {
        using var workspace = new DuplicateWorkspace();
        workspace.WriteFile("folder/keep.txt", "duplicate-bytes");
        workspace.WriteFile("folder/copy.txt", "duplicate-bytes");
        var progressEvents = new ConcurrentQueue<DuplicateAnalyzeProgress>();
        var service = new DuplicateFileAnalysisService();

        await service.AnalyzeAsync(
            new WindowsMountedVolume(workspace.RootPath),
            hideMacOsSystemFiles: true,
            excludedPathPatterns: null,
            includeSubfolders: true,
            progress: new Progress<DuplicateAnalyzeProgress>(progressEvents.Enqueue));

        Assert.Contains(progressEvents, progress => progress.Phase == DuplicateAnalyzePhase.Scanning);
        Assert.Contains(progressEvents, progress => progress.Phase == DuplicateAnalyzePhase.Hashing && progress.HashedFiles > 0);
    }

    [Fact]
    public async Task AnalyzeAsync_CanCancelDuringHashing()
    {
        using var workspace = new DuplicateWorkspace();
        for (var index = 0; index < 12; index++)
        {
            workspace.WriteFile($"folder/file{index:00}.txt", $"duplicate-{index % 3}");
        }

        var service = new DuplicateFileAnalysisService();
        using var cancellationTokenSource = new CancellationTokenSource();

        var analyzeTask = service.AnalyzeAsync(
            new WindowsMountedVolume(workspace.RootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            cancellationToken: cancellationTokenSource.Token,
            progress: new Progress<DuplicateAnalyzeProgress>(progress =>
            {
                if (progress.Phase == DuplicateAnalyzePhase.Hashing && progress.HashedFiles >= 1)
                {
                    cancellationTokenSource.Cancel();
                }
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzeTask);
    }

    private sealed class DuplicateWorkspace : IDisposable
    {
        public DuplicateWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void WriteFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
