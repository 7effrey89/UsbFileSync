using System.IO;
using System.Windows.Forms;

namespace UsbFileSync.App.Services;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(initialPath) ? initialPath : string.Empty,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}