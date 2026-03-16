using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public interface ICloudRootProvider
{
    IReadOnlyList<CloudRootDefinition> GetAvailableRoots();
}

public sealed record CloudRootDefinition(string RootPath, string DisplayText, IVolumeSource Volume);