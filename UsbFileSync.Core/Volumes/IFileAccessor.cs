namespace UsbFileSync.Core.Volumes;

public interface IFileAccessor
{
    Stream OpenRead(string path);

    Stream OpenWrite(string path, bool overwrite = true);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    void Move(string sourcePath, string destinationPath, bool overwrite = false);

    void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc);
}
