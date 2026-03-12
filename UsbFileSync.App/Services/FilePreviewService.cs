using System.IO;
using System.Windows.Media.Imaging;

namespace UsbFileSync.App.Services;

public sealed class FilePreviewService
{
    private const int PreviewCharacterLimit = 8000;

    private readonly IReadOnlyDictionary<string, PreviewProviderKind> _providerMappings;

    public FilePreviewService(
        IReadOnlyDictionary<string, string>? providerOverrides = null,
        IShellPreviewHandlerResolver? shellPreviewHandlerResolver = null)
    {
        _providerMappings = PreviewProviderDefaults.MergeWithOverrides(providerOverrides);
    }

    public FilePreviewResult Load(string? fullPath)
    {
        var normalizedPath = fullPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
        {
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.None,
                Message = "No File",
            };
        }

        var extension = PreviewProviderDefaults.NormalizeExtension(Path.GetExtension(normalizedPath));
        if (!_providerMappings.TryGetValue(extension, out var providerKind))
        {
            providerKind = PreviewProviderKind.Unsupported;
        }

        return providerKind switch
        {
            PreviewProviderKind.Text => LoadTextPreview(normalizedPath),
            PreviewProviderKind.Image => LoadImagePreview(normalizedPath),
            PreviewProviderKind.Office => LoadOfficePreview(normalizedPath),
            PreviewProviderKind.Pdf => new FilePreviewResult { Kind = FilePreviewKind.Pdf, FilePath = normalizedPath },
            PreviewProviderKind.Media => new FilePreviewResult { Kind = FilePreviewKind.Media, FilePath = normalizedPath },
            _ => new FilePreviewResult { Kind = FilePreviewKind.Unsupported, FilePath = normalizedPath, Message = "Preview for item type not supported" },
        };
    }

    private FilePreviewResult LoadOfficePreview(string path)
    {
        OfficePreviewExtractionResult? extractionResult = null;

        try
        {
            extractionResult = OfficePreviewExtractor.ExtractPreview(path, PreviewCharacterLimit);
        }
        catch (Exception)
        {
            extractionResult = null;
        }

        var fallbackText = extractionResult?.PreviewText ?? string.Empty;
        var diagnosticText = extractionResult?.DiagnosticText ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Text,
                FilePath = path,
                TextContent = fallbackText,
            };
        }

        if (!string.IsNullOrWhiteSpace(diagnosticText))
        {
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Text,
                FilePath = path,
                TextContent = diagnosticText,
            };
        }

        return new FilePreviewResult
        {
            Kind = FilePreviewKind.Unsupported,
            FilePath = path,
            Message = "Preview could not be loaded.",
        };
    }

    private static FilePreviewResult LoadTextPreview(string path)
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
                preview = "File is empty.";
            }
            else if (!reader.EndOfStream)
            {
                preview += Environment.NewLine + Environment.NewLine + "[Preview truncated]";
            }

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Text,
                FilePath = path,
                TextContent = preview,
            };
        }
        catch (Exception)
        {
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Unsupported,
                FilePath = path,
                Message = "Preview could not be loaded.",
            };
        }
    }

    private static FilePreviewResult LoadImagePreview(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Image,
                FilePath = path,
                ImageSource = image,
            };
        }
        catch (Exception)
        {
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Unsupported,
                FilePath = path,
                Message = "Preview for item type not supported",
            };
        }
    }
}