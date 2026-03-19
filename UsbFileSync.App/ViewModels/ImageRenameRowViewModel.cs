using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ImageSource = System.Windows.Media.ImageSource;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UsbFileSync.App.ViewModels;

public sealed class ImageRenameRowViewModel : ObservableObject
{
    private static readonly Brush DefaultPathBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly Brush PendingChevronTrackBrush = CreateFrozenBrush(198, 239, 206);
    private static readonly Brush PendingChevronFillBrush = CreateFrozenBrush(78, 167, 46);
    private static readonly Brush PendingChevronTextBrush = Brushes.White;
    private static readonly Brush PendingChevronTipBrush = CreateFrozenBrush(56, 118, 29);
    private static readonly Brush CompletedChevronTrackBrush = CreateFrozenBrush(228, 228, 228);
    private static readonly Brush CompletedChevronFillBrush = CreateFrozenBrush(146, 146, 146);
    private static readonly Brush CompletedChevronTextBrush = Brushes.White;
    private static readonly Brush CompletedChevronTipBrush = CreateFrozenBrush(98, 98, 98);

    private bool _isSelected;

    public ImageRenameRowViewModel(
        ImageRenamePlanItem planItem,
        bool isSelected = false,
        IFileIconProvider? iconProvider = null,
        IDriveDisplayNameService? driveDisplayNameService = null)
    {
        PlanItem = planItem;
        _isSelected = isSelected && !planItem.IsCompleted;
        OpenPath = planItem.SourceFullPath;
        DisplayPath = (driveDisplayNameService ?? new WindowsDriveDisplayNameService())
            .FormatPathForDisplay(planItem.SourceFullPath);
        IconSource = string.IsNullOrWhiteSpace(OpenPath)
            ? null
            : (iconProvider ?? ShellFileIconProvider.Instance).GetIcon(OpenPath, isDirectory: false);
        IconGlyph = "\uE8A5";
    }

    public ImageRenamePlanItem PlanItem { get; }

    public string ItemKey => PlanItem.SourceRelativePath;

    public string CurrentFileName => PlanItem.CurrentFileName;

    public string ProposedFileName => PlanItem.ProposedFileName;

    public string DisplayPath { get; }

    public string OpenPath { get; }

    public bool HasOpenPath => !string.IsNullOrWhiteSpace(OpenPath);

    public bool IsMatchedByFilePattern => PlanItem.IsMatchedByFileNameMask;

    public bool IsCompleted => PlanItem.IsCompleted;

    public bool CanSelect => !IsCompleted;

    public string FilePatternText => string.IsNullOrWhiteSpace(PlanItem.MatchedFileNameMask)
        ? "No File Pattern Match"
        : PlanItem.MatchedFileNameMask;

    public string TimestampText => PlanItem.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss");

    public string StatusText => IsCompleted
        ? "Completed"
        : PlanItem.UsedCollisionSuffix
            ? "Sequenced"
            : "Ready";

    public ImageSource? IconSource { get; }

    public string IconGlyph { get; }

    public Brush PathBrush => DefaultPathBrush;

    public string RenameChevronText => IsCompleted ? "Completed" : "Rename";

    public Brush RenameChevronTrackBrush => IsCompleted ? CompletedChevronTrackBrush : PendingChevronTrackBrush;

    public Brush RenameChevronFillBrush => IsCompleted ? CompletedChevronFillBrush : PendingChevronFillBrush;

    public Brush RenameChevronTextBrush => IsCompleted ? CompletedChevronTextBrush : PendingChevronTextBrush;

    public Brush RenameChevronTipBrush => IsCompleted ? CompletedChevronTipBrush : PendingChevronTipBrush;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect)
            {
                return;
            }

            SetProperty(ref _isSelected, value);
        }
    }

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
