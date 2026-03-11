using System.Windows.Media;

namespace UsbFileSync.App.Services;

public sealed class FilePreviewResult
{
    public required FilePreviewKind Kind { get; init; }

    public string Message { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string TextContent { get; init; } = string.Empty;

    public ImageSource? ImageSource { get; init; }
}