using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

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
