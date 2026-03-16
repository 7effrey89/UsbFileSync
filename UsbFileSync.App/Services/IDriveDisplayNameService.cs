namespace UsbFileSync.App.Services;

public interface IDriveDisplayNameService
{
    string FormatPathForDisplay(string path);
    string FormatDestinationPathForDisplay(string path);
}