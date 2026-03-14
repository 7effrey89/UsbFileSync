using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public interface ISourceVolumeService
{
    bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason);
}
