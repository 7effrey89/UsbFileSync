namespace UsbFileSync.Core.Volumes;

public sealed class ApfsVolumeSource : DirectoryBackedVolumeSource
{
    public ApfsVolumeSource(string id, string displayName, string backingRoot, string? root = null)
        : base(
            id,
            displayName,
            fileSystemType: "APFS",
            isReadOnly: true,
            root: string.IsNullOrWhiteSpace(root) ? $"apfs://{id}" : root,
            backingRoot,
            useWindowsDisplayPaths: false)
    {
    }
}
