using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UsbFileSync.App.Services;

public sealed class ShellFileIconProvider : IFileIconProvider
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    public static ShellFileIconProvider Instance { get; } = new();

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private ShellFileIconProvider()
    {
    }

    public ImageSource? GetIcon(string path, bool isDirectory)
    {
        var cacheKey = isDirectory
            ? "__folder"
            : string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? "__file"
                : Path.GetExtension(path);

        return _cache.GetOrAdd(cacheKey, _ => ExtractIcon(path, isDirectory));
    }

    private static ImageSource? ExtractIcon(string path, bool isDirectory)
    {
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiSmallIcon;
        string lookupPath;

        if (isDirectory)
        {
            flags |= ShgfiUseFileAttributes;
            lookupPath = string.IsNullOrWhiteSpace(path) ? "folder" : path;
        }
        else if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            lookupPath = path;
        }
        else
        {
            flags |= ShgfiUseFileAttributes;
            var extension = Path.GetExtension(path);
            lookupPath = string.IsNullOrWhiteSpace(extension) ? "file" : $"file{extension}";
        }

        var fileInfo = new SHFILEINFO();
        var result = SHGetFileInfo(lookupPath, attributes, ref fileInfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(fileInfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DestroyIcon(fileInfo.hIcon);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}