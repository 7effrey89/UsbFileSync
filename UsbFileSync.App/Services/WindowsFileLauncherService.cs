using System.Diagnostics;
using System.IO;

namespace UsbFileSync.App.Services;

public sealed class WindowsFileLauncherService : IFileLauncherService
{
    public void OpenItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            OpenFolder(path);
            return;
        }

        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
                return;
            }
            catch
            {
                OpenFile(path);
                return;
            }
        }

        var containingFolder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(containingFolder) && Directory.Exists(containingFolder))
        {
            OpenFolder(containingFolder);
        }
    }

    public void OpenContainingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var folderPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            OpenFolder(folderPath);
        }
    }

    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}