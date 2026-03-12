namespace UsbFileSync.Core.Volumes;

public interface IFileEntry
{
    string FullPath { get; }

    string Name { get; }

    bool IsDirectory { get; }

    long? Size { get; }

    DateTime? LastWriteTimeUtc { get; }

    bool Exists { get; }
}
