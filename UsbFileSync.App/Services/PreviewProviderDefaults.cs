namespace UsbFileSync.App.Services;

public static class PreviewProviderDefaults
{
    private static readonly Dictionary<string, PreviewProviderKind> DefaultMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = PreviewProviderKind.Text,
        [".log"] = PreviewProviderKind.Text,
        [".md"] = PreviewProviderKind.Text,
        [".json"] = PreviewProviderKind.Text,
        [".ipynb"] = PreviewProviderKind.Text,
        [".xml"] = PreviewProviderKind.Text,
        [".csv"] = PreviewProviderKind.Text,
        [".yml"] = PreviewProviderKind.Text,
        [".yaml"] = PreviewProviderKind.Text,
        [".cfg"] = PreviewProviderKind.Text,
        [".ini"] = PreviewProviderKind.Text,
        [".config"] = PreviewProviderKind.Text,
        [".py"] = PreviewProviderKind.Text,
        [".cs"] = PreviewProviderKind.Text,
        [".xaml"] = PreviewProviderKind.Text,
        [".csproj"] = PreviewProviderKind.Text,
        [".sln"] = PreviewProviderKind.Text,
        [".sql"] = PreviewProviderKind.Text,
        [".ps1"] = PreviewProviderKind.Text,
        [".bat"] = PreviewProviderKind.Text,
        [".cmd"] = PreviewProviderKind.Text,
        [".html"] = PreviewProviderKind.Text,
        [".htm"] = PreviewProviderKind.Text,
        [".js"] = PreviewProviderKind.Text,
        [".ts"] = PreviewProviderKind.Text,
        [".tsx"] = PreviewProviderKind.Text,
        [".jsx"] = PreviewProviderKind.Text,
        [".java"] = PreviewProviderKind.Text,
        [".cpp"] = PreviewProviderKind.Text,
        [".c"] = PreviewProviderKind.Text,
        [".h"] = PreviewProviderKind.Text,
        [".hpp"] = PreviewProviderKind.Text,
        [".css"] = PreviewProviderKind.Text,
        [".png"] = PreviewProviderKind.Image,
        [".jpg"] = PreviewProviderKind.Image,
        [".jpeg"] = PreviewProviderKind.Image,
        [".gif"] = PreviewProviderKind.Image,
        [".bmp"] = PreviewProviderKind.Image,
        [".tif"] = PreviewProviderKind.Image,
        [".tiff"] = PreviewProviderKind.Image,
        [".ico"] = PreviewProviderKind.Image,
        [".heic"] = PreviewProviderKind.Image,
        [".heif"] = PreviewProviderKind.Image,
        [".pdf"] = PreviewProviderKind.Pdf,
        [".mp3"] = PreviewProviderKind.Media,
        [".wav"] = PreviewProviderKind.Media,
        [".m4a"] = PreviewProviderKind.Media,
        [".aac"] = PreviewProviderKind.Media,
        [".mp4"] = PreviewProviderKind.Media,
        [".mkv"] = PreviewProviderKind.Media,
        [".mov"] = PreviewProviderKind.Media,
        [".avi"] = PreviewProviderKind.Media,
        [".wmv"] = PreviewProviderKind.Media,
    };

    public static IReadOnlyDictionary<string, PreviewProviderKind> GetAll() => DefaultMappings;

    public static Dictionary<string, string> CreateSerializableMapping() => DefaultMappings.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, PreviewProviderKind> MergeWithOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, PreviewProviderKind>(DefaultMappings, StringComparer.OrdinalIgnoreCase);
        if (overrides is null)
        {
            return merged;
        }

        foreach (var pair in overrides)
        {
            var normalizedExtension = NormalizeExtension(pair.Key);
            if (string.IsNullOrWhiteSpace(normalizedExtension))
            {
                continue;
            }

            if (Enum.TryParse<PreviewProviderKind>(pair.Value, ignoreCase: true, out var providerKind))
            {
                merged[normalizedExtension] = providerKind;
            }
        }

        return merged;
    }

    public static string NormalizeExtension(string? extension)
    {
        var normalized = extension?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        return normalized.ToLowerInvariant();
    }
}