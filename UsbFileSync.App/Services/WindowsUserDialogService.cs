using System.Windows;

namespace UsbFileSync.App.Services;

public sealed class WindowsUserDialogService : IUserDialogService
{
    public void ShowWarning(string title, string message)
    {
        System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }
}
