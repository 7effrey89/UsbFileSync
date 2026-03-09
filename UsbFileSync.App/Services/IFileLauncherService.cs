namespace UsbFileSync.App.Services;

public interface IFileLauncherService
{
    void OpenItem(string path);

    void OpenContainingFolder(string path);

    void OpenFile(string path);
}