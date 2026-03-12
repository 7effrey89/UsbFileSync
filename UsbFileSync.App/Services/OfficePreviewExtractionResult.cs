namespace UsbFileSync.App.Services;

public sealed class OfficePreviewExtractionResult
{
    public string PreviewText { get; init; } = string.Empty;

    public string DiagnosticText { get; init; } = string.Empty;

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewText);
}