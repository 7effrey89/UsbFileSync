using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

public interface IImageRenameCityResolver
{
    string? TryResolveCity(IVolumeSource volume, string relativePath, string extension);
}
