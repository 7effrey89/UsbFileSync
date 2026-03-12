namespace UsbFileSync.Core.Volumes;

public sealed class ReadOnlyVolumeException : InvalidOperationException
{
    public ReadOnlyVolumeException(string volumeName)
        : base($"The volume '{volumeName}' is read-only.")
    {
    }
}
