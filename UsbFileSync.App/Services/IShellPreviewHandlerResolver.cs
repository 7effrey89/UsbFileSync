namespace UsbFileSync.App.Services;

public interface IShellPreviewHandlerResolver
{
    bool TryGetPreviewHandlerClsid(string filePath, out Guid previewHandlerClsid);
}