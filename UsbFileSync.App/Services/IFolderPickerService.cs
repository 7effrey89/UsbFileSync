namespace UsbFileSync.App.Services;

public interface IFolderPickerService
{
    string? PickFolder(string title, string? initialPath);
}