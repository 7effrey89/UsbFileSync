using System.IO;
using System.Management;
using System.Security.Principal;
using System.Reflection;

namespace UsbFileSync.App.Services;

internal static class SharpExt4DiskAccessor
{
    private static readonly ConstructorInfo? DiskPathConstructor = typeof(SharpExt4.ExtDisk).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [typeof(string)],
        modifiers: null);

    public static SharpExt4.ExtDisk OpenDiskWithRetry(
        int diskNumber,
        Action<string>? log = null,
        int maxAttempts = 5,
        int delayMilliseconds = 1000)
        => OpenDiskWithRetry(
            diskNumber,
            log,
            maxAttempts,
            delayMilliseconds,
            SharpExt4.ExtDisk.Open,
            TryOpenDiskFromDevicePath,
            WindowsDiskMetadata.TryRepairSharpExt4Metadata,
            RefreshWindowsStorageCache,
            Thread.Sleep);

    internal static SharpExt4.ExtDisk OpenDiskWithRetry(
        int diskNumber,
        Action<string>? log,
        int maxAttempts,
        int delayMilliseconds,
        Func<int, SharpExt4.ExtDisk> openByNumber,
        Func<int, Action<string>?, Action<int, SharpExt4.ExtDisk, Action<string>?>, SharpExt4.ExtDisk?> openByDevicePath,
        Action<int, SharpExt4.ExtDisk, Action<string>?> repairMetadata,
        Action<Action<string>?> refreshWindowsStorageCache,
        Action<int> delay)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                log?.Invoke($"Opening PhysicalDrive{diskNumber} with SharpExt4 (attempt {attempt}/{maxAttempts})...");
                var disk = openByNumber(diskNumber);
                repairMetadata(diskNumber, disk, log);
                return disk;
            }
            catch (Exception ex)
            {
                lastError = ex;
                log?.Invoke($"SharpExt4 open failed: {ex.Message}");

                var fallbackDisk = openByDevicePath(diskNumber, log, repairMetadata);
                if (fallbackDisk is not null)
                {
                    return fallbackDisk;
                }

                if (attempt == maxAttempts)
                {
                    break;
                }

                refreshWindowsStorageCache(log);
                delay(delayMilliseconds);
            }
        }

        throw new IOException(
            $"SharpExt4 could not open PhysicalDrive{diskNumber} after {maxAttempts} attempts. See the application log for the detailed exception trail.",
            lastError);
    }

    internal static SharpExt4.ExtDisk? TryOpenDiskFromDevicePath(
        int diskNumber,
        Action<string>? log,
        Action<int, SharpExt4.ExtDisk, Action<string>?> repairMetadata)
    {
        if (DiskPathConstructor is null)
        {
            return null;
        }

        try
        {
            log?.Invoke($"Retrying PhysicalDrive{diskNumber} via SharpExt4 raw device-path fallback...");
            var disk = (SharpExt4.ExtDisk?)DiskPathConstructor.Invoke([$"\\\\.\\PhysicalDrive{diskNumber}"]);
            if (disk is null)
            {
                return null;
            }

            repairMetadata(diskNumber, disk, log);
            return disk;
        }
        catch (Exception ex)
        {
            log?.Invoke($"SharpExt4 raw device-path fallback failed: {ex.Message}");
            return null;
        }
    }

    public static void RefreshWindowsStorageCache(Action<string>? log = null)
    {
        var scriptPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(scriptPath, "rescan" + Environment.NewLine + "exit" + Environment.NewLine);
            log?.Invoke("Refreshing Windows storage cache...");

            var process = new System.Diagnostics.ProcessStartInfo("diskpart.exe", $"/s \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = System.Diagnostics.Process.Start(process)
                ?? throw new IOException("Failed to start diskpart.exe for storage rescan.");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30_000);

            if (proc.ExitCode != 0)
            {
                throw new IOException(
                    $"diskpart rescan failed with exit code {proc.ExitCode}. STDERR: {stderr.Trim()} STDOUT: {stdout.Trim()}");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: storage cache refresh failed: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
            }
        }
    }
}

internal static class WindowsWriteAccess
{
    public static bool HasRequiredWriteAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal static class WindowsPhysicalDriveResolver
{
    public static (bool Success, int DiskNumber) ResolveDiskNumber(string rootPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(rootPath))
        {
            return (false, -1);
        }

        var normalizedRootPath = NormalizeRootPath(rootPath);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT DeviceID FROM Win32_DiskDrive");

            foreach (ManagementObject drive in searcher.Get())
            {
                var deviceId = drive["DeviceID"]?.ToString() ?? string.Empty;
                if (!TryParsePhysicalDriveNumber(deviceId, out var diskNumber))
                {
                    continue;
                }

                foreach (ManagementObject partition in drive.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject logicalDisk in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        var logicalDiskId = logicalDisk["DeviceID"]?.ToString();
                        if (string.IsNullOrWhiteSpace(logicalDiskId))
                        {
                            continue;
                        }

                        var logicalRoot = logicalDiskId.EndsWith(":", StringComparison.Ordinal)
                            ? logicalDiskId + Path.DirectorySeparatorChar
                            : logicalDiskId;

                        if (string.Equals(logicalRoot, normalizedRootPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return (true, diskNumber);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return (false, -1);
    }

    private static string NormalizeRootPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        return root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
    }

    private static bool TryParsePhysicalDriveNumber(string deviceId, out int diskNumber)
    {
        diskNumber = -1;
        const string prefix = "\\\\.\\PHYSICALDRIVE";

        return deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(deviceId[prefix.Length..], out diskNumber);
    }
}

internal static class WindowsDiskMetadata
{
    private static readonly System.Reflection.FieldInfo CapacityField =
        typeof(SharpExt4.ExtDisk).GetField("capacity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate SharpExt4.ExtDisk.capacity field.");

    private static readonly System.Reflection.FieldInfo PartitionsField =
        typeof(SharpExt4.ExtDisk).GetField("partitions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate SharpExt4.ExtDisk.partitions field.");

    private static readonly System.Reflection.FieldInfo PartitionOffsetField =
        typeof(SharpExt4.Partition).GetField("<backing_store>Offset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate SharpExt4.Partition offset field.");

    private static readonly System.Reflection.FieldInfo PartitionSizeField =
        typeof(SharpExt4.Partition).GetField("<backing_store>Size", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate SharpExt4.Partition size field.");

    public static void TryRepairSharpExt4Metadata(int diskNumber, SharpExt4.ExtDisk disk, Action<string>? log = null)
    {
        if (!NeedsRepair(disk))
        {
            return;
        }

        try
        {
            var metadata = ReadDiskMetadata(diskNumber);
            if (metadata is null || metadata.Partitions.Count == 0)
            {
                return;
            }

            var repairedPartitions = new List<SharpExt4.Partition>(metadata.Partitions.Count);
            foreach (var partition in metadata.Partitions)
            {
                var repairedPartition = new SharpExt4.Partition();
                PartitionOffsetField.SetValue(repairedPartition, partition.Offset);
                PartitionSizeField.SetValue(repairedPartition, partition.Size);
                repairedPartitions.Add(repairedPartition);
            }

            if (disk.Capacity == 0 && metadata.SizeBytes > 0)
            {
                CapacityField.SetValue(disk, metadata.SizeBytes);
            }

            PartitionsField.SetValue(disk, repairedPartitions);
            log?.Invoke("SharpExt4 returned invalid partition metadata; using Windows partition layout as a fallback.");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: could not repair SharpExt4 metadata from Windows partition data: {ex.Message}");
        }
    }

    private static bool NeedsRepair(SharpExt4.ExtDisk disk) =>
        disk.Capacity == 0
        || disk.Partitions is null
        || disk.Partitions.Count == 0
        || disk.Partitions.All(partition => partition.Offset == 0 && partition.Size == 0);

    private static DiskMetadata? ReadDiskMetadata(int diskNumber)
    {
        using var diskSearcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            $"SELECT Size FROM Win32_DiskDrive WHERE Index = {diskNumber}");

        ulong sizeBytes = 0;
        foreach (ManagementObject disk in diskSearcher.Get())
        {
            sizeBytes = ulong.TryParse(disk["Size"]?.ToString(), out var parsedSizeBytes) ? parsedSizeBytes : 0;
            break;
        }

        using var partitionSearcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            $"SELECT Index, Size, StartingOffset FROM Win32_DiskPartition WHERE DiskIndex = {diskNumber}");

        var partitions = new List<PartitionMetadata>();
        foreach (ManagementObject partition in partitionSearcher.Get())
        {
            var offset = ulong.TryParse(partition["StartingOffset"]?.ToString(), out var parsedOffset) ? parsedOffset : 0;
            var size = ulong.TryParse(partition["Size"]?.ToString(), out var parsedSize) ? parsedSize : 0;
            var index = uint.TryParse(partition["Index"]?.ToString(), out var parsedIndex) ? parsedIndex : uint.MaxValue;

            if (offset == 0 || size == 0)
            {
                continue;
            }

            partitions.Add(new PartitionMetadata(index, offset, size));
        }

        partitions.Sort((left, right) => left.Index.CompareTo(right.Index));
        return new DiskMetadata(sizeBytes, partitions);
    }

    private sealed record DiskMetadata(ulong SizeBytes, List<PartitionMetadata> Partitions);

    private sealed record PartitionMetadata(uint Index, ulong Offset, ulong Size);
}