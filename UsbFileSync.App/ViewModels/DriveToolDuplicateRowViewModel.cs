using System.IO;
using System.Windows;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace UsbFileSync.App.ViewModels;

public sealed class DriveToolDuplicateRowViewModel : ObservableObject
{
    private const byte NeutralRed = 32;
    private const byte NeutralGreen = 32;
    private const byte NeutralBlue = 32;
    private const byte KeepRed = 98;
    private const byte KeepGreen = 98;
    private const byte KeepBlue = 98;
    private const byte DeleteRed = 196;
    private const byte DeleteGreen = 43;
    private const byte DeleteBlue = 28;

    private static readonly Brush DefaultPathBrush = CreateFrozenBrush(NeutralRed, NeutralGreen, NeutralBlue);
    private static readonly Brush KeepActionBrush = CreateFrozenBrush(KeepRed, KeepGreen, KeepBlue);
    private static readonly Brush DeleteActionBrush = CreateFrozenBrush(DeleteRed, DeleteGreen, DeleteBlue);

    private readonly bool _canSelect;
    private bool _isSelected;
    private bool _hasSelectionConflict;

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

    public Brush PathBrush => DefaultPathBrush;

    public Brush NamePrefixBrush => IsGroupHeader
        ? NameBrush
        : KeepActionBrush;

    public Brush NameBrush => IsGroupHeader && HasSelectionConflict ? DeleteActionBrush : DefaultPathBrush;

    public bool CanSelect => _canSelect;

    public bool HasSelectionConflict
    {
        get => _hasSelectionConflict;
        set
        {
            if (SetProperty(ref _hasSelectionConflict, value))
            {
                RaisePropertyChanged(nameof(NameBrush));
            }
        }
    }

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

    public Brush ActionBrush => IsGroupHeader
        ? DefaultPathBrush
        : IsSelected
            ? DeleteActionBrush
            : KeepActionBrush;

    public Thickness NameMargin => IsGroupHeader ? new Thickness(0) : new Thickness(22, 0, 0, 0);

    public FontWeight NameFontWeight => IsGroupHeader ? FontWeights.SemiBold : FontWeights.Normal;

    public string NamePrefix => IsGroupHeader ? "SHA-256" : "↳";

    public string FileName => FileEntry is null ? DisplayName : Path.GetFileName(FileEntry.FullPath);

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
