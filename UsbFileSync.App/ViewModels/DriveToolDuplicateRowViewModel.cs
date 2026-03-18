using System.IO;
using System.Windows;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class DriveToolDuplicateRowViewModel : ObservableObject
{
    private static readonly System.Windows.Media.Brush DefaultPathBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly System.Windows.Media.Brush KeepActionBrush = CreateFrozenBrush(98, 98, 98);
    private static readonly System.Windows.Media.Brush DeleteActionBrush = CreateFrozenBrush(196, 43, 28);

    private readonly bool _canSelect;
    private bool _isSelected;

    public DriveToolDuplicateRowViewModel(
        string itemKey,
        string groupKey,
        bool isGroupHeader,
        string displayName,
        string displayPath,
        string checksumText,
        string fileType,
        string sizeText,
        DuplicateFileEntry? fileEntry = null,
        IFileIconProvider? iconProvider = null)
    {
        ItemKey = itemKey;
        GroupKey = groupKey;
        IsGroupHeader = isGroupHeader;
        DisplayName = displayName;
        DisplayPath = displayPath;
        ChecksumText = checksumText;
        FileType = fileType;
        SizeText = sizeText;
        FileEntry = fileEntry;
        _canSelect = !isGroupHeader && fileEntry is not null;
        OpenPath = fileEntry?.FullPath ?? string.Empty;
        IconSource = isGroupHeader || string.IsNullOrWhiteSpace(OpenPath)
            ? null
            : (iconProvider ?? ShellFileIconProvider.Instance).GetIcon(OpenPath, isDirectory: false);
        IconGlyph = isGroupHeader ? string.Empty : "\uE8A5";
    }

    public string ItemKey { get; }

    public string GroupKey { get; }

    public bool IsGroupHeader { get; }

    public string DisplayName { get; }

    public string DisplayPath { get; }

    public string ChecksumText { get; }

    public string FileType { get; }

    public string SizeText { get; }

    public DuplicateFileEntry? FileEntry { get; }

    public string OpenPath { get; }

    public bool HasOpenPath => !string.IsNullOrWhiteSpace(OpenPath);

    public ImageSource? IconSource { get; }

    public string IconGlyph { get; }

    public System.Windows.Media.Brush PathBrush => DefaultPathBrush;

    public bool CanSelect => _canSelect;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!_canSelect)
            {
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                RaisePropertyChanged(nameof(ActionText));
                RaisePropertyChanged(nameof(ActionBrush));
            }
        }
    }

    public string ActionText => IsGroupHeader
        ? "Review"
        : IsSelected
            ? "Delete"
            : "Keep";

    public System.Windows.Media.Brush ActionBrush => IsGroupHeader
        ? DefaultPathBrush
        : IsSelected
            ? DeleteActionBrush
            : KeepActionBrush;

    public Thickness NameMargin => IsGroupHeader ? new Thickness(0) : new Thickness(22, 0, 0, 0);

    public FontWeight NameFontWeight => IsGroupHeader ? FontWeights.SemiBold : FontWeights.Normal;

    public string NamePrefix => IsGroupHeader ? "SHA-256" : "↳";

    public string FileName => FileEntry is null ? DisplayName : Path.GetFileName(FileEntry.FullPath);

    private static System.Windows.Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
