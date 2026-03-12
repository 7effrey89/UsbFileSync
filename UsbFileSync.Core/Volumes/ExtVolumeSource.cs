namespace UsbFileSync.Core.Volumes;

public sealed class ExtVolumeSource : DirectoryBackedVolumeSource
{
    public ExtVolumeSource(string id, string displayName, string backingRoot, string? root = null)
        : base(
            id,
            displayName,
            fileSystemType: "ext4",
            isReadOnly: false,
            root: string.IsNullOrWhiteSpace(root) ? $"ext4://{id}" : root,
            backingRoot,
            useWindowsDisplayPaths: false)
    {
    }
}
