using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UsbFileSync.App.ViewModels;

public sealed class FileComparisonDialogViewModel
{
    public FileComparisonDialogViewModel(SyncPreviewRowViewModel row)
    {
        SourcePane = FileComparisonPaneViewModel.Create("Source", row.SourcePath, row.SourceSize, row.SourceModified);
        DestinationPane = FileComparisonPaneViewModel.Create("Destination", row.DestinationPath, row.DestinationSize, row.DestinationModified);
    }

    public FileComparisonPaneViewModel SourcePane { get; }

    public FileComparisonPaneViewModel DestinationPane { get; }
}

public sealed class FileComparisonPaneViewModel
{
    private const int PreviewCharacterLimit = 8000;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".csv", ".yml", ".yaml", ".ini", ".config",
        ".cs", ".xaml", ".csproj", ".sln", ".sql", ".ps1", ".bat", ".cmd", ".html", ".htm",
        ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp", ".css"
    };

    private FileComparisonPaneViewModel(
        string sideLabel,
        string fileName,
        string sizeText,
        string modifiedText,
        string fullPath,
        string previewText,
        ImageSource? previewImageSource,
        bool hasFile)
    {
        SideLabel = sideLabel;
        FileName = fileName;
        SizeText = string.IsNullOrWhiteSpace(sizeText) ? "-" : sizeText;
        ModifiedText = string.IsNullOrWhiteSpace(modifiedText) ? "-" : modifiedText;
        FullPath = fullPath;
        PreviewText = previewText;
        PreviewImageSource = previewImageSource;
        HasFile = hasFile;
    }

    public string SideLabel { get; }

    public string FileName { get; }

    public string SizeText { get; }

    public string ModifiedText { get; }

    public string FullPath { get; }

    public bool HasFile { get; }

    public bool HasPath => !string.IsNullOrWhiteSpace(FullPath);

    public string PreviewText { get; }

    public ImageSource? PreviewImageSource { get; }

    public bool HasImagePreview => PreviewImageSource is not null;

    public bool HasTextPreview => !string.IsNullOrWhiteSpace(PreviewText);

    public static FileComparisonPaneViewModel Create(string sideLabel, string? fullPath, string sizeText, string modifiedText)
    {
        var normalizedPath = fullPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return new FileComparisonPaneViewModel(sideLabel, string.Empty, sizeText, modifiedText, string.Empty, "No file", null, false);
        }

        var fileName = Path.GetFileName(normalizedPath);
        if (Directory.Exists(normalizedPath))
        {
            return new FileComparisonPaneViewModel(sideLabel, string.IsNullOrWhiteSpace(fileName) ? normalizedPath : fileName, sizeText, modifiedText, normalizedPath, "Preview for item type not supported", null, true);
        }

        if (!File.Exists(normalizedPath))
        {
            return new FileComparisonPaneViewModel(sideLabel, string.Empty, sizeText, modifiedText, string.Empty, "No file", null, false);
        }

        var extension = Path.GetExtension(normalizedPath);
        if (ImageExtensions.Contains(extension))
        {
            var imageSource = TryLoadImage(normalizedPath);
            if (imageSource is not null)
            {
                return new FileComparisonPaneViewModel(sideLabel, fileName, sizeText, modifiedText, normalizedPath, string.Empty, imageSource, true);
            }
        }

        if (TextExtensions.Contains(extension))
        {
            return new FileComparisonPaneViewModel(sideLabel, fileName, sizeText, modifiedText, normalizedPath, ReadTextPreview(normalizedPath), null, true);
        }

        return new FileComparisonPaneViewModel(sideLabel, fileName, sizeText, modifiedText, normalizedPath, "Preview for item type not supported", null, true);
    }

    private static string ReadTextPreview(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[PreviewCharacterLimit];
            var read = reader.Read(buffer, 0, buffer.Length);
            var preview = new string(buffer, 0, read);
            if (string.IsNullOrWhiteSpace(preview))
            {
                return "File is empty.";
            }

            if (!reader.EndOfStream)
            {
                preview += Environment.NewLine + Environment.NewLine + "[Preview truncated]";
            }

            return preview;
        }
        catch (Exception)
        {
            return "Preview could not be loaded.";
        }
    }

    private static ImageSource? TryLoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}