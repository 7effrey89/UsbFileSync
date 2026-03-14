namespace UsbFileSync.Core.Volumes;

public sealed class ExtVolumeSource : DirectoryBackedVolumeSource
{
    private readonly Func<bool> _hasRequiredWriteAccess;

    public ExtVolumeSource(string id, string displayName, string backingRoot, string? root = null)
        : this(id, displayName, backingRoot, root, ExtVolumeWriteAccess.HasRequiredWriteAccess)
    {
    }

    internal ExtVolumeSource(string id, string displayName, string backingRoot, string? root, Func<bool> hasRequiredWriteAccess)
        : base(
            id,
            displayName,
            fileSystemType: "ext4",
            isReadOnly: false,
            root: string.IsNullOrWhiteSpace(root) ? $"ext4://{id}" : root,
            backingRoot,
            useWindowsDisplayPaths: false)
    {
        _hasRequiredWriteAccess = hasRequiredWriteAccess ?? throw new ArgumentNullException(nameof(hasRequiredWriteAccess));
    }

    protected override void EnsureWritable()
    {
        base.EnsureWritable();

        if (!_hasRequiredWriteAccess())
        {
            throw new UnauthorizedAccessException("Writing to ext4 volumes requires elevated access. Run UsbFileSync as administrator when an ext4 volume is a sync destination.");
        }
    }
}
