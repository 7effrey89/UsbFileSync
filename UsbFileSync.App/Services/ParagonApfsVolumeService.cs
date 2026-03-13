using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.App.Services;

public sealed class ParagonApfsVolumeService : ISourceVolumeService
{
    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!OperatingSystem.IsWindows() || !LooksLikeDriveRoot(path))
        {
            return false;
        }

        var executablePath = TryFindHelperExecutable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            failureReason = "The Paragon APFS helper executable was not found.";
            return false;
        }

        var normalizedRootPath = NormalizeRootPath(path);
        var probeResult = RunTextCommand(executablePath, "enumjson", normalizedRootPath);
        if (!probeResult.Success)
        {
            failureReason = NormalizeProbeFailure(probeResult.ErrorMessage, normalizedRootPath);
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(probeResult.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                failureReason = "The Paragon APFS helper returned an unexpected response.";
                return false;
            }
        }
        catch (JsonException)
        {
            failureReason = "The Paragon APFS helper returned unreadable JSON output.";
            return false;
        }

        volume = new ParagonApfsVolumeSource(normalizedRootPath, executablePath);
        return true;
    }

    private static bool LooksLikeDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var rootPath = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            return string.Equals(
                NormalizeRootPath(path),
                NormalizeRootPath(rootPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizeRootPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath) ?? fullPath;
        return rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
    }

    private static string? TryFindHelperExecutable()
    {
        var candidateFileNames = new[]
        {
            "apfsutil.exe",
            Path.Combine("vendor", "paragon_apfs_sdk_ce", ".build", "bin", "RelWithDebInfo", "apfsutil.exe"),
            Path.Combine("vendor", "paragon_apfs_sdk_ce", ".build", "bin", "Debug", "apfsutil.exe"),
            Path.Combine("vendor", "paragon_apfs_sdk_ce", ".build", "bin", "Release", "apfsutil.exe"),
        };

        var explicitPath = Environment.GetEnvironmentVariable("USBFILESYNC_APFSUTIL_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        foreach (var baseDirectory in EnumerateSearchBaseDirectories())
        {
            foreach (var candidateFileName in candidateFileNames)
            {
                var candidatePath = Path.IsPathRooted(candidateFileName)
                    ? candidateFileName
                    : Path.Combine(baseDirectory, candidateFileName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchBaseDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startDirectory in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            {
                continue;
            }

            var directory = new DirectoryInfo(startDirectory);
            while (directory is not null && seen.Add(directory.FullName))
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }
    }

    private static ProcessResult RunTextCommand(string executablePath, string commandName, string targetPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        process.StartInfo.ArgumentList.Add(commandName);
        process.StartInfo.ArgumentList.Add(targetPath);

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        var success = process.ExitCode == 0;
        var errorMessage = success
            ? string.Empty
            : string.IsNullOrWhiteSpace(standardError)
                ? standardOutput.Trim()
                : standardError.Trim();

        if (!success && string.IsNullOrWhiteSpace(errorMessage))
        {
            errorMessage = $"The Paragon APFS helper exited with code {process.ExitCode}.";
        }

        return new ProcessResult(success, standardOutput, errorMessage);
    }

    internal static string NormalizeProbeFailure(string? rawErrorMessage, string path)
    {
        if (string.IsNullOrWhiteSpace(rawErrorMessage))
        {
            return "The Paragon APFS helper could not open the selected drive.";
        }

        var normalizedMessage = rawErrorMessage.Trim();

        if (normalizedMessage.Contains("Unknown FileSystem", StringComparison.OrdinalIgnoreCase)
            || normalizedMessage.Contains("a0001006", StringComparison.OrdinalIgnoreCase))
        {
            return $"The selected drive '{path}' does not appear to contain an APFS volume.";
        }

        if (normalizedMessage.Contains("Error 0x2", StringComparison.OrdinalIgnoreCase))
        {
            return $"The selected drive '{path}' is not currently available. Reconnect the drive and try again.";
        }

        return normalizedMessage;
    }

    internal static bool IsNotApfsFailure(string? failureReason) =>
        !string.IsNullOrWhiteSpace(failureReason)
        && failureReason.Contains("does not appear to contain an APFS volume", StringComparison.OrdinalIgnoreCase);

    private sealed record ProcessResult(bool Success, string StandardOutput, string ErrorMessage);

    private sealed class ParagonApfsVolumeSource(string deviceRootPath, string executablePath) : IVolumeSource
    {
        public string Id => $"apfs::{deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)}";

        public string DisplayName => $"APFS ({deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)})";

        public string FileSystemType => "APFS";

        public bool IsReadOnly => true;

        public string Root => deviceRootPath;

        public IFileEntry GetEntry(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return new ProcessVolumeFileEntry(Root, DisplayName, true, null, null, true);
            }

            var parentRelativePath = GetParentRelativePath(normalizedRelativePath);
            var targetName = GetLeafName(normalizedRelativePath);
            var entry = Enumerate(parentRelativePath)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase));

            return entry ?? new ProcessVolumeFileEntry(BuildDisplayPath(normalizedRelativePath), targetName, false, null, null, false);
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            var commandTargetPath = BuildCommandTargetPath(path);
            var result = RunTextCommand(executablePath, "enumjson", commandTargetPath);
            if (!result.Success)
            {
                return Array.Empty<IFileEntry>();
            }

            try
            {
                using var document = JsonDocument.Parse(result.StandardOutput);
                return document.RootElement
                    .EnumerateArray()
                    .Select(entry => CreateFileEntry(NormalizeRelativePath(path), entry))
                    .ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<IFileEntry>();
            }
        }

        public Stream OpenRead(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                throw new FileNotFoundException("A file path is required.", BuildDisplayPath(path));
            }

            var tempPath = Path.GetTempFileName();
            try
            {
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                };

                process.StartInfo.ArgumentList.Add("readraw");
                process.StartInfo.ArgumentList.Add(BuildCommandTargetPath(normalizedRelativePath));

                process.Start();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                process.StandardOutput.BaseStream.CopyTo(fileStream);
                process.WaitForExit();

                var standardError = standardErrorTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    throw new IOException(string.IsNullOrWhiteSpace(standardError)
                        ? $"The Paragon APFS helper exited with code {process.ExitCode}."
                        : standardError.Trim());
                }

                return new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }

        public Stream OpenWrite(string path, bool overwrite = true) => throw new ReadOnlyVolumeException(DisplayName);

        public void CreateDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteFile(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new ReadOnlyVolumeException(DisplayName);

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new ReadOnlyVolumeException(DisplayName);

        private IFileEntry CreateFileEntry(string parentRelativePath, JsonElement entry)
        {
            var name = entry.GetProperty("name").GetString() ?? string.Empty;
            var entryRelativePath = string.IsNullOrEmpty(parentRelativePath)
                ? name
                : parentRelativePath + "/" + name;
            var isDirectory = entry.GetProperty("isDirectory").GetBoolean();
            var size = entry.GetProperty("size").GetInt64();
            var lastWriteTimeUtc = TryReadFileTimeUtc(entry, "modifiedTime");
            return new ProcessVolumeFileEntry(
                BuildDisplayPath(entryRelativePath),
                name,
                isDirectory,
                isDirectory ? null : size,
                lastWriteTimeUtc,
                true);
        }

        private string BuildCommandTargetPath(string? relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return deviceRootPath;
            }

            var windowsRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(deviceRootPath, windowsRelativePath);
        }

        private string BuildDisplayPath(string? relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return Root;
            }

            var windowsRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Root, windowsRelativePath);
        }

        private static string NormalizeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return relativePath
                .Replace('\\', '/')
                .Trim('/');
        }

        private static string GetParentRelativePath(string relativePath)
        {
            var lastSeparatorIndex = relativePath.LastIndexOf('/');
            return lastSeparatorIndex < 0 ? string.Empty : relativePath[..lastSeparatorIndex];
        }

        private static string GetLeafName(string relativePath)
        {
            var lastSeparatorIndex = relativePath.LastIndexOf('/');
            return lastSeparatorIndex < 0 ? relativePath : relativePath[(lastSeparatorIndex + 1)..];
        }

        private static DateTime? TryReadFileTimeUtc(JsonElement entry, string propertyName)
        {
            if (!entry.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            if (!property.TryGetUInt64(out var fileTime) || fileTime == 0 || fileTime > long.MaxValue)
            {
                return null;
            }

            try
            {
                return DateTime.FromFileTimeUtc((long)fileTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private sealed record ProcessVolumeFileEntry(
            string FullPath,
            string Name,
            bool IsDirectory,
            long? Size,
            DateTime? LastWriteTimeUtc,
            bool Exists) : IFileEntry;
    }
}