using Microsoft.Win32;
using System.IO;

namespace UsbFileSync.App.Services;

public sealed class WindowsShellPreviewHandlerResolver : IShellPreviewHandlerResolver
{
    private const string PreviewHandlerCategoryId = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    public bool TryGetPreviewHandlerClsid(string filePath, out Guid previewHandlerClsid)
    {
        previewHandlerClsid = Guid.Empty;

        var extension = PreviewProviderDefaults.NormalizeExtension(Path.GetExtension(filePath));
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        if (TryReadPreviewHandlerClsid(extension, out previewHandlerClsid))
        {
            return true;
        }

        if (TryReadPreviewHandlerClsid($@"SystemFileAssociations\{extension}", out previewHandlerClsid))
        {
            return true;
        }

        using var extensionKey = Registry.ClassesRoot.OpenSubKey(extension);
        var progId = extensionKey?.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(progId)
            && TryReadPreviewHandlerClsid(progId, out previewHandlerClsid))
        {
            return true;
        }

        var perceivedType = extensionKey?.GetValue("PerceivedType") as string;
        return !string.IsNullOrWhiteSpace(perceivedType)
            && TryReadPreviewHandlerClsid($@"SystemFileAssociations\{perceivedType}", out previewHandlerClsid);
    }

    private static bool TryReadPreviewHandlerClsid(string classOrKeyPath, out Guid previewHandlerClsid)
    {
        previewHandlerClsid = Guid.Empty;

        using var key = Registry.ClassesRoot.OpenSubKey($@"{classOrKeyPath}\shellex\{PreviewHandlerCategoryId}");
        var clsidText = key?.GetValue(null) as string;
        return Guid.TryParse(clsidText, out previewHandlerClsid);
    }
}