namespace UsbFileSync.Core.Volumes;

public interface IVolumeSource : IFileAccessor
{
    string Id { get; }

    string DisplayName { get; }

    string FileSystemType { get; }

    bool IsReadOnly { get; }

    string Root { get; }

    IFileEntry GetEntry(string path);

    IEnumerable<IFileEntry> Enumerate(string path);
}
