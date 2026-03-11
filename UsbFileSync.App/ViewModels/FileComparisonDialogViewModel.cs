using System.IO;
using UsbFileSync.App.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class FileComparisonDialogViewModel
{
    public FileComparisonDialogViewModel(
        string sourcePath,
        string sourceSize,
        string sourceModified,
        string destinationPath,
        string destinationSize,
        string destinationModified,
        IReadOnlyDictionary<string, string>? previewProviderMappings = null)
    {
        var previewService = new FilePreviewService(previewProviderMappings);
        SourcePane = FileComparisonPaneViewModel.Create("Source", sourcePath, sourceSize, sourceModified, previewService);
        DestinationPane = FileComparisonPaneViewModel.Create("Destination", destinationPath, destinationSize, destinationModified, previewService);
    }

    public FileComparisonDialogViewModel(SyncPreviewRowViewModel row, IReadOnlyDictionary<string, string>? previewProviderMappings = null)
        : this(row.SourcePath, row.SourceSize, row.SourceModified, row.DestinationPath, row.DestinationSize, row.DestinationModified, previewProviderMappings)
    {
    }

    public FileComparisonPaneViewModel SourcePane { get; }

    public FileComparisonPaneViewModel DestinationPane { get; }
}

public sealed class FileComparisonPaneViewModel
{
    private FileComparisonPaneViewModel(
        string sideLabel,
        string fileName,
        string sizeText,
        string modifiedText,
        string fullPath,
        FilePreviewResult preview)
    {
        SideLabel = sideLabel;
        FileName = fileName;
        SizeText = string.IsNullOrWhiteSpace(sizeText) ? "-" : sizeText;
        ModifiedText = string.IsNullOrWhiteSpace(modifiedText) ? "-" : modifiedText;
        FullPath = fullPath;
        Preview = preview;
    }

    public string SideLabel { get; }

    public string FileName { get; }

    public string SizeText { get; }

    public string ModifiedText { get; }

    public string FullPath { get; }

    public FilePreviewResult Preview { get; }

    public bool HasFile => Preview.Kind != FilePreviewKind.None;

    public bool HasPath => !string.IsNullOrWhiteSpace(FullPath);

    public string PreviewText => Preview.Kind switch
    {
        FilePreviewKind.Text => Preview.TextContent,
        FilePreviewKind.Unsupported => Preview.Message,
        FilePreviewKind.None => Preview.Message,
        _ => string.Empty,
    };

    public bool HasImagePreview => Preview.Kind == FilePreviewKind.Image && Preview.ImageSource is not null;

    public bool HasPdfPreview => Preview.Kind == FilePreviewKind.Pdf && !string.IsNullOrWhiteSpace(Preview.FilePath);

    public bool HasMediaPreview => Preview.Kind == FilePreviewKind.Media && !string.IsNullOrWhiteSpace(Preview.FilePath);

    public bool HasTextPreview => !string.IsNullOrWhiteSpace(PreviewText);

    public static FileComparisonPaneViewModel Create(string sideLabel, string? fullPath, string sizeText, string modifiedText, FilePreviewService previewService)
    {
        var normalizedPath = fullPath?.Trim() ?? string.Empty;
        var preview = previewService.Load(normalizedPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return new FileComparisonPaneViewModel(sideLabel, string.Empty, sizeText, modifiedText, string.Empty, preview);
        }

        var fileName = Path.GetFileName(normalizedPath);
        if (!File.Exists(normalizedPath))
        {
            return new FileComparisonPaneViewModel(sideLabel, string.Empty, sizeText, modifiedText, string.Empty, preview);
        }

        return new FileComparisonPaneViewModel(sideLabel, fileName, sizeText, modifiedText, normalizedPath, preview);
    }
}