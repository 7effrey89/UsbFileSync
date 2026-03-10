using System.Windows.Media;

namespace UsbFileSync.App.Services;

public interface IFileIconProvider
{
    ImageSource? GetIcon(string path, bool isDirectory);
}