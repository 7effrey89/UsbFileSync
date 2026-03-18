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

        var plan = Assert.Single(result.RenameSuggestions.Where(candidate => candidate.IsMatchedByFileNameMask));
        Assert.Equal("IMG_1234.JPG", plan.CurrentFileName);
        Assert.Equal("20240305_101112_IMG_1234.jpg", plan.ProposedFileName);
        Assert.Equal(2, result.CandidateFileCount);
        Assert.Equal(1, result.MatchedMaskCandidateCount);
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
            result.RenameSuggestions.OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
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

        var applied = service.ApplyRenames(volume, result.RenameSuggestions, CancellationToken.None);

        Assert.Equal(1, applied);
        Assert.False(File.Exists(Path.Combine(_rootPath, "DCIM", "IMG_1234.jpg")));
        Assert.True(File.Exists(Path.Combine(_rootPath, "DCIM", "20240305_101112_IMG_1234.jpg")));
    }

    [Fact]
    public void Analyze_IncludesFilesOutsideTheConfiguredMaskScope()
    {
        WriteFile("IMG_1234.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        WriteFile("notes.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));

        var service = new ImageRenameAnalysisService();

        var result = service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileName,
            ["IMG_????"],
            [".jpg"]);

        Assert.Equal(2, result.RenameSuggestions.Count);
        Assert.Equal(2, result.CandidateFileCount);
        Assert.Equal(1, result.MatchedMaskCandidateCount);
        Assert.Collection(
            result.RenameSuggestions.OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
            matched =>
            {
                Assert.Equal("IMG_1234.jpg", matched.CurrentFileName);
                Assert.True(matched.IsMatchedByFileNameMask);
                Assert.Equal("IMG_????", matched.MatchedFileNameMask);
            },
            unmatched =>
            {
                Assert.Equal("notes.jpg", unmatched.CurrentFileName);
                Assert.False(unmatched.IsMatchedByFileNameMask);
                Assert.Equal(string.Empty, unmatched.MatchedFileNameMask);
                Assert.Equal("20240305_101112_notes.jpg", unmatched.ProposedFileName);
            });
    }

    [Fact]
    public void Analyze_UsesResolvedCityForCityPattern()
    {
        WriteFile("IMG_1234.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        var cityResolver = new StubImageRenameCityResolver("Berlin");
        var service = new ImageRenameAnalysisService(cityResolver);

        var result = service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileNameCity,
            ["IMG_????"],
            [".jpg"]);

        var plan = Assert.Single(result.RenameSuggestions);
        Assert.Equal("20240305_101112_IMG_1234_Berlin.jpg", plan.ProposedFileName);
        Assert.Equal(["IMG_1234.jpg"], cityResolver.RequestedPaths);
    }

    [Fact]
    public void TryReadGpsCoordinates_ReadsExifGpsCoordinatesFromJpeg()
    {
        var jpegBytes = CreateExifJpegWithGps(52, 30, 0, 'N', 13, 24, 0, 'E');
        using var stream = new MemoryStream(jpegBytes);

        var success = ImageRenameCityResolver.TryReadGpsCoordinates(stream, out var coordinates);

        Assert.True(success);
        Assert.Equal(52.5d, coordinates.Latitude, precision: 4);
        Assert.Equal(13.4d, coordinates.Longitude, precision: 4);
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

    private static byte[] CreateExifJpegWithGps(
        uint latitudeDegrees,
        uint latitudeMinutes,
        uint latitudeSeconds,
        char latitudeRef,
        uint longitudeDegrees,
        uint longitudeMinutes,
        uint longitudeSeconds,
        char longitudeRef)
    {
        using var exifStream = new MemoryStream();
        using (var writer = new BinaryWriter(exifStream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((byte)'E');
            writer.Write((byte)'x');
            writer.Write((byte)'i');
            writer.Write((byte)'f');
            writer.Write((byte)0);
            writer.Write((byte)0);

            var tiffStart = exifStream.Position;
            writer.Write((byte)'I');
            writer.Write((byte)'I');
            writer.Write((ushort)42);
            writer.Write((uint)8);

            writer.Write((ushort)1);
            writer.Write((ushort)0x8825);
            writer.Write((ushort)4);
            writer.Write((uint)1);
            writer.Write((uint)26);
            writer.Write((uint)0);

            writer.Write((ushort)4);

            writer.Write((ushort)0x0001);
            writer.Write((ushort)2);
            writer.Write((uint)2);
            writer.Write((byte)latitudeRef);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);

            writer.Write((ushort)0x0002);
            writer.Write((ushort)5);
            writer.Write((uint)3);
            writer.Write((uint)80);

            writer.Write((ushort)0x0003);
            writer.Write((ushort)2);
            writer.Write((uint)2);
            writer.Write((byte)longitudeRef);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);

            writer.Write((ushort)0x0004);
            writer.Write((ushort)5);
            writer.Write((uint)3);
            writer.Write((uint)104);

            writer.Write((uint)0);

            WriteRational(writer, latitudeDegrees, 1);
            WriteRational(writer, latitudeMinutes, 1);
            WriteRational(writer, latitudeSeconds, 1);
            WriteRational(writer, longitudeDegrees, 1);
            WriteRational(writer, longitudeMinutes, 1);
            WriteRational(writer, longitudeSeconds, 1);
        }

        var exifBytes = exifStream.ToArray();
        using var jpegStream = new MemoryStream();
        using var jpegWriter = new BinaryWriter(jpegStream, System.Text.Encoding.ASCII, leaveOpen: true);
        jpegWriter.Write((byte)0xFF);
        jpegWriter.Write((byte)0xD8);
        jpegWriter.Write((byte)0xFF);
        jpegWriter.Write((byte)0xE1);
        jpegWriter.Write((byte)((exifBytes.Length + 2) >> 8));
        jpegWriter.Write((byte)((exifBytes.Length + 2) & 0xFF));
        jpegWriter.Write(exifBytes);
        jpegWriter.Write((byte)0xFF);
        jpegWriter.Write((byte)0xD9);
        return jpegStream.ToArray();
    }

    private static void WriteRational(BinaryWriter writer, uint numerator, uint denominator)
    {
        writer.Write(numerator);
        writer.Write(denominator);
    }

    private sealed class StubImageRenameCityResolver(string? city) : IImageRenameCityResolver
    {
        public List<string> RequestedPaths { get; } = [];

        public string? TryResolveCity(UsbFileSync.Core.Volumes.IVolumeSource volume, string relativePath, string extension)
        {
            RequestedPaths.Add(relativePath);
            return city;
        }
    }
}
