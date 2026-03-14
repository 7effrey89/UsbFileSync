namespace UsbFileSync.Core.Volumes;

public sealed class ReadOnlyVolumeException : InvalidOperationException
{
    public string VolumeName { get; }

    public ReadOnlyVolumeException(string volumeName)
        : base(BuildMessage(volumeName))
    {
        VolumeName = volumeName;
    }

    private static string BuildMessage(string volumeName)
    {
        if (LooksLikeLinuxVolume(volumeName))
        {
            return $"The volume '{volumeName}' is currently in read-only mode. Write mode for Linux volumes is available only when UsbFileSync is opened with elevated privileges and the drive can be opened through the bundled ext4 writer.";
        }

        return $"The volume '{volumeName}' is read-only.";
    }

    private static bool LooksLikeLinuxVolume(string? volumeName) =>
        !string.IsNullOrWhiteSpace(volumeName)
        && volumeName.Contains("ext", StringComparison.OrdinalIgnoreCase);
}
