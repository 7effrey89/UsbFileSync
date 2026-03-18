using System.IO;
using System.Windows;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class DriveToolDuplicateRowViewModel : ObservableObject
{
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
        string actionText,
        DuplicateFileEntry? fileEntry = null)
    {
        ItemKey = itemKey;
        GroupKey = groupKey;
        IsGroupHeader = isGroupHeader;
        DisplayName = displayName;
        DisplayPath = displayPath;
        ChecksumText = checksumText;
        FileType = fileType;
        SizeText = sizeText;
        ActionText = actionText;
        FileEntry = fileEntry;
        _canSelect = !isGroupHeader && fileEntry is not null;
    }

    public string ItemKey { get; }

    public string GroupKey { get; }

    public bool IsGroupHeader { get; }

    public string DisplayName { get; }

    public string DisplayPath { get; }

    public string ChecksumText { get; }

    public string FileType { get; }

    public string SizeText { get; }

    public string ActionText { get; }

    public DuplicateFileEntry? FileEntry { get; }

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

            SetProperty(ref _isSelected, value);
        }
    }

    public Thickness NameMargin => IsGroupHeader ? new Thickness(0) : new Thickness(22, 0, 0, 0);

    public FontWeight NameFontWeight => IsGroupHeader ? FontWeights.SemiBold : FontWeights.Normal;

    public string NamePrefix => IsGroupHeader ? "SHA-256" : "↳";

    public string FileName => FileEntry is null ? DisplayName : Path.GetFileName(FileEntry.FullPath);
}
