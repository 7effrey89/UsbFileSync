using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class ImageRenameAnalysisServiceTests : IDisposable
{
    private readonly string _rootPath;

    public ImageRenameAnalysisServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.ImageRenameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public void Analyze_FiltersToConfiguredCameraPatternsAndExtensions()
    {
        WriteFile("IMG_1234.JPG", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        WriteFile("notes.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        WriteFile("IMG_5555.png", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));

        var service = new ImageRenameAnalysisService();

        var result = service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileName,
            ["IMG_????"],
            [".jpg"]);

        var plan = Assert.Single(result.PlannedRenames);
        Assert.Equal("IMG_1234.JPG", plan.CurrentFileName);
        Assert.Equal("20240305_101112_IMG_1234.jpg", plan.ProposedFileName);
        Assert.Equal(1, result.CandidateFileCount);
    }

    [Fact]
    public void Analyze_AddsSequencerWhenTargetsWouldCollide()
    {
        var timestamp = new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc);
        WriteFile("IMG_0001.jpg", timestamp);
        WriteFile("IMG_0002.jpg", timestamp);
        WriteFile("20240305_101112.jpg", timestamp);

        var service = new ImageRenameAnalysisService();

        var result = service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOnly,
            ["IMG_????"],
            [".jpg"]);

        Assert.Collection(
            result.PlannedRenames.OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
            first =>
            {
                Assert.Equal("IMG_0001.jpg", first.CurrentFileName);
                Assert.Equal("20240305_101112_001.jpg", first.ProposedFileName);
                Assert.True(first.UsedCollisionSuffix);
            },
            second =>
            {
                Assert.Equal("IMG_0002.jpg", second.CurrentFileName);
                Assert.Equal("20240305_101112_002.jpg", second.ProposedFileName);
                Assert.True(second.UsedCollisionSuffix);
            });
    }

    [Fact]
    public void ApplyRenames_MovesFilesToPlannedNames()
    {
        var timestamp = new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc);
        WriteFile("DCIM/IMG_1234.jpg", timestamp);

        var volume = new WindowsMountedVolume(_rootPath);
        var service = new ImageRenameAnalysisService();
        var result = service.Analyze(
            volume,
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileName,
            ["IMG_????"],
            [".jpg"]);

        var applied = service.ApplyRenames(volume, result.PlannedRenames, CancellationToken.None);

        Assert.Equal(1, applied);
        Assert.False(File.Exists(Path.Combine(_rootPath, "DCIM", "IMG_1234.jpg")));
        Assert.True(File.Exists(Path.Combine(_rootPath, "DCIM", "20240305_101112_IMG_1234.jpg")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private void WriteFile(string relativePath, DateTime lastWriteTimeUtc)
    {
        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "image");
        File.SetLastWriteTimeUtc(fullPath, lastWriteTimeUtc);
    }
}
