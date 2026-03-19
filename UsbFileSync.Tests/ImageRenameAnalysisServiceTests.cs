using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;
using System.Collections.Concurrent;
using System.Text.Json;

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
        Assert.Equal(0, result.CompletedCandidateCount);
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
            result.RenameSuggestions.Where(plan => !plan.IsCompleted).OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
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
        Assert.Equal(0, result.CompletedCandidateCount);
        Assert.Collection(
            result.RenameSuggestions.OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
            matched =>
            {
                Assert.Equal("IMG_1234.jpg", matched.CurrentFileName);
                Assert.True(matched.IsMatchedByFileNameMask);
                Assert.False(matched.IsCompleted);
                Assert.Equal("IMG_????", matched.MatchedFileNameMask);
            },
            unmatched =>
            {
                Assert.Equal("notes.jpg", unmatched.CurrentFileName);
                Assert.False(unmatched.IsMatchedByFileNameMask);
                Assert.False(unmatched.IsCompleted);
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
    public void Analyze_TracksCompletedFilesThatAlreadyMatchTheTargetFormat()
    {
        WriteFile("20240305_101112_IMG_1234.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        WriteFile("IMG_1234.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));

        var service = new ImageRenameAnalysisService();

        var result = service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileName,
            ["IMG_????"],
            [".jpg"]);

        Assert.Equal(2, result.CandidateFileCount);
        Assert.Equal(1, result.CompletedCandidateCount);
        Assert.Equal(2, result.RenameSuggestions.Count);

        Assert.Collection(
            result.RenameSuggestions.OrderBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase),
            completed =>
            {
                Assert.Equal("20240305_101112_IMG_1234.jpg", completed.CurrentFileName);
                Assert.True(completed.IsCompleted);
                Assert.Equal(completed.SourceRelativePath, completed.ProposedRelativePath);
                Assert.Equal(completed.CurrentFileName, completed.ProposedFileName);
            },
            pending =>
            {
                Assert.Equal("IMG_1234.jpg", pending.CurrentFileName);
                Assert.False(pending.IsCompleted);
            });
    }

    [Fact]
    public void Analyze_ReportsScanningAndPlanningProgress()
    {
        WriteFile("IMG_1234.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        WriteFile("notes.jpg", new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc));
        var progressEvents = new ConcurrentQueue<ImageRenameAnalyzeProgress>();
        var service = new ImageRenameAnalysisService();

        service.Analyze(
            new WindowsMountedVolume(_rootPath),
            hideMacOsSystemFiles: false,
            excludedPathPatterns: null,
            includeSubfolders: true,
            ImageRenamePatternKind.TimestampOriginalFileName,
            ["IMG_????"],
            [".jpg"],
            progress: new Progress<ImageRenameAnalyzeProgress>(progressEvents.Enqueue));

        Assert.Contains(progressEvents, progress => progress.Phase == ImageRenameAnalyzePhase.Scanning);
        Assert.Contains(progressEvents, progress => progress.Phase == ImageRenameAnalyzePhase.Planning && progress.ProcessedFiles > 0);
    }

    [Fact]
    public void Analyze_CanCancelDuringScanning()
    {
        var service = new ImageRenameAnalysisService();
        var volume = SlowImageRenameVolumeSource.CreateFileTree(fileCount: 48);
        using var cancellationTokenSource = new CancellationTokenSource();

        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(50));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            service.Analyze(
                volume,
                hideMacOsSystemFiles: false,
                excludedPathPatterns: null,
                includeSubfolders: true,
                ImageRenamePatternKind.TimestampOriginalFileName,
                ["IMG_????"],
                [".jpg"],
                cancellationToken: cancellationTokenSource.Token));
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

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    [InlineData(".heic")]
    [InlineData(".heif")]
    [InlineData(".avif")]
    [InlineData(".png")]
    [InlineData(".gif")]
    public void SupportsMetadataLookup_IncludesModernImageFormats(string extension)
    {
        Assert.True(ImageRenameCityResolver.SupportsMetadataLookup(extension));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void SupportsMetadataLookup_RejectsUnsupportedFormats(string extension)
    {
        Assert.False(ImageRenameCityResolver.SupportsMetadataLookup(extension));
    }

    [Fact]
    public void DefaultExtensions_IncludeTiffFormats()
    {
        Assert.Contains(".tif", ImageRenameDefaults.GetDefaultExtensions());
        Assert.Contains(".tiff", ImageRenameDefaults.GetDefaultExtensions());
    }

    [Fact]
    public void GetPreferredReverseGeocodeLanguages_PrefersEnglishThenFallsBackToLocal()
    {
        Assert.Equal(["en", null], ImageRenameCityResolver.GetPreferredReverseGeocodeLanguages(ImageRenameCityLanguagePreference.EnglishThenLocal));
    }

    [Fact]
    public void GetPreferredReverseGeocodeLanguages_PrefersLocalThenFallsBackToEnglish()
    {
        Assert.Equal([null, "en"], ImageRenameCityResolver.GetPreferredReverseGeocodeLanguages(ImageRenameCityLanguagePreference.LocalThenEnglish));
    }

    [Fact]
    public void CreateReverseGeocodeRequest_AddsEnglishAcceptLanguageWhenRequested()
    {
        using var request = ImageRenameCityResolver.CreateReverseGeocodeRequest((55.6761d, 12.5683d), "en");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&addressdetails=1&lat=55.676100&lon=12.568300", request.RequestUri?.ToString());
        Assert.Equal("en", Assert.Single(request.Headers.AcceptLanguage).Value);
    }

    [Fact]
    public void CreateReverseGeocodeRequest_OmitsAcceptLanguageForLocalFallback()
    {
        using var request = ImageRenameCityResolver.CreateReverseGeocodeRequest((55.6761d, 12.5683d), preferredLanguage: null);

        Assert.Empty(request.Headers.AcceptLanguage);
    }

    [Fact]
    public void TryExtractCityName_UsesAvailableLocalizedAddressField()
    {
        using var document = JsonDocument.Parse("""
            {
              "address": {
                "municipality": "Kobenhavn"
              }
            }
            """);

        var city = ImageRenameCityResolver.TryExtractCityName(document.RootElement);

        Assert.Equal("Kobenhavn", city);
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

    private sealed class SlowImageRenameVolumeSource : IVolumeSource
    {
        private readonly Dictionary<string, List<SlowImageRenameFileEntry>> _entriesByDirectory;

        private SlowImageRenameVolumeSource(Dictionary<string, List<SlowImageRenameFileEntry>> entriesByDirectory)
        {
            _entriesByDirectory = entriesByDirectory;
        }

        public string Id => "slow-image-rename";

        public string DisplayName => "Slow Image Rename";

        public string FileSystemType => "NTFS";

        public bool IsReadOnly => false;

        public string Root => "SlowRenameRoot";

        public static SlowImageRenameVolumeSource CreateFileTree(int fileCount)
        {
            var entriesByDirectory = new Dictionary<string, List<SlowImageRenameFileEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [string.Empty] = []
            };

            for (var index = 0; index < fileCount; index++)
            {
                var directory = $"folder{index / 8:00}";
                if (!entriesByDirectory.ContainsKey(directory))
                {
                    entriesByDirectory[directory] = [];
                    entriesByDirectory[string.Empty].Add(new SlowImageRenameFileEntry($"SlowRenameRoot\\{directory}", directory, IsDirectory: true, null, DateTime.UtcNow, Exists: true));
                }

                var relativePath = $"{directory}/IMG_{index:0000}.jpg";
                entriesByDirectory[directory].Add(new SlowImageRenameFileEntry($"SlowRenameRoot\\{relativePath.Replace('/', '\\')}", $"IMG_{index:0000}.jpg", IsDirectory: false, 32, new DateTime(2024, 3, 5, 10, 11, 12, DateTimeKind.Utc), Exists: true));
            }

            return new SlowImageRenameVolumeSource(entriesByDirectory);
        }

        public IFileEntry GetEntry(string path)
        {
            var normalizedPath = Normalize(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return new SlowImageRenameFileEntry(Root, Root, IsDirectory: true, null, DateTime.UtcNow, Exists: true);
            }

            if (_entriesByDirectory.ContainsKey(normalizedPath))
            {
                return new SlowImageRenameFileEntry($"{Root}\\{normalizedPath.Replace('/', '\\')}", Path.GetFileName(normalizedPath), IsDirectory: true, null, DateTime.UtcNow, Exists: true);
            }

            foreach (var entries in _entriesByDirectory.Values)
            {
                var match = entries.FirstOrDefault(entry => string.Equals(Normalize(entry.RelativePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return new SlowImageRenameFileEntry($"{Root}\\{normalizedPath.Replace('/', '\\')}", Path.GetFileName(normalizedPath), IsDirectory: false, null, null, Exists: false);
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            Thread.Sleep(150);
            return _entriesByDirectory.TryGetValue(Normalize(path), out var entries)
                ? entries.ToArray()
                : Array.Empty<IFileEntry>();
        }

        public Stream OpenRead(string path) => new MemoryStream([1, 2, 3], writable: false);

        public Stream OpenWrite(string path, bool overwrite = true) => throw new NotSupportedException();

        public void DeleteFile(string path) => throw new NotSupportedException();

        public void DeleteDirectory(string path) => throw new NotSupportedException();

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new NotSupportedException();

        public void CreateDirectory(string path) => throw new NotSupportedException();

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new NotSupportedException();

        private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
    }

    private sealed record SlowImageRenameFileEntry(string FullPath, string Name, bool IsDirectory, long? Size, DateTime? LastWriteTimeUtc, bool Exists) : IFileEntry
    {
        public string RelativePath => FullPath.Replace("SlowRenameRoot\\", string.Empty, StringComparison.Ordinal).Replace('\\', '/');
    }
}
