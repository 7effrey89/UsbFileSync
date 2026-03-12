namespace UsbFileSync.Core.Volumes;

public sealed class WindowsMountedVolume : DirectoryBackedVolumeSource
{
    public WindowsMountedVolume(string rootPath)
        : base(
            id: $"windows::{Path.GetFullPath(rootPath)}",
            displayName: rootPath,
            fileSystemType: "Windows",
            isReadOnly: false,
            root: Path.GetFullPath(rootPath),
            backingRoot: rootPath,
            useWindowsDisplayPaths: true)
    {
    }
}
