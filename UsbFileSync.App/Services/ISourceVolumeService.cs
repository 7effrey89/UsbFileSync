using UsbFileSync.Core.Volumes;

namespace UsbFileSync.App.Services;

public interface ISourceVolumeService
{
    bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason);
}