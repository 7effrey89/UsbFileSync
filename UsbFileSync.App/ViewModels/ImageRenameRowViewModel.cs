using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using System.IO;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ImageSource = System.Windows.Media.ImageSource;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UsbFileSync.App.ViewModels;

public sealed class ImageRenameRowViewModel : ObservableObject
{
    private enum ImageRenameRowState
    {
        Pending,
        Completed,
        Renamed,
    }

    private const double ChevronVisualWidth = 120d;

    private static readonly Brush DefaultPathBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly Brush PendingChevronTrackBrush = CreateFrozenBrush(198, 239, 206);
    private static readonly Brush PendingChevronFillBrush = CreateFrozenBrush(78, 167, 46);
    private static readonly Brush PendingChevronTextBrush = CreateFrozenBrush(0, 97, 0);
    private static readonly Brush PendingChevronTipBrush = CreateFrozenBrush(56, 118, 29);
    private static readonly Brush CompletedChevronTrackBrush = CreateFrozenBrush(228, 228, 228);
    private static readonly Brush CompletedChevronFillBrush = CreateFrozenBrush(146, 146, 146);
    private static readonly Brush CompletedChevronTextBrush = Brushes.White;
    private static readonly Brush CompletedChevronTipBrush = CreateFrozenBrush(98, 98, 98);

    private readonly IDriveDisplayNameService _driveDisplayNameService;
    private readonly bool _showsInPrimaryCategory;
    private ImageRenamePlanItem _planItem;
    private bool _isSelected;
    private string _openPath;
    private string _displayPath;
    private ImageRenameRowState _state;

    public ImageRenameRowViewModel(
        ImageRenamePlanItem planItem,
        bool isSelected = false,
        IFileIconProvider? iconProvider = null,
        IDriveDisplayNameService? driveDisplayNameService = null)
    {
        _driveDisplayNameService = driveDisplayNameService ?? new WindowsDriveDisplayNameService();
        _planItem = planItem;
        _state = planItem.IsCompleted ? ImageRenameRowState.Completed : ImageRenameRowState.Pending;
        _showsInPrimaryCategory = !planItem.IsCompleted;
        _isSelected = isSelected && !planItem.IsCompleted;
        _openPath = planItem.SourceFullPath;
        _displayPath = _driveDisplayNameService.FormatPathForDisplay(planItem.SourceFullPath);
        IconSource = string.IsNullOrWhiteSpace(_openPath)
            ? null
            : (iconProvider ?? ShellFileIconProvider.Instance).GetIcon(_openPath, isDirectory: false);
        IconGlyph = "\uE8A5";
    }

    public ImageRenamePlanItem PlanItem => _planItem;

    public bool ShowsInPrimaryCategory => _showsInPrimaryCategory;

    public string ItemKey => PlanItem.SourceRelativePath;

    public string CurrentFileName => PlanItem.CurrentFileName;

    public string ProposedFileName => PlanItem.ProposedFileName;

    public string DisplayPath => _displayPath;

    public string OpenPath => _openPath;

    public bool HasOpenPath => !string.IsNullOrWhiteSpace(OpenPath);

    public bool IsMatchedByFilePattern => PlanItem.IsMatchedByFileNameMask;

    public bool IsCompleted => _state is ImageRenameRowState.Completed or ImageRenameRowState.Renamed;

    public bool IsRenamed => _state == ImageRenameRowState.Renamed;

    public bool CanSelect => !IsCompleted;

    public string FilePatternText => string.IsNullOrWhiteSpace(PlanItem.MatchedFileNameMask)
        ? "No File Pattern Match"
        : PlanItem.MatchedFileNameMask;

    public string TimestampText => PlanItem.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss");

    public string StatusText => _state switch
    {
        ImageRenameRowState.Completed => "Completed",
        ImageRenameRowState.Renamed => "Renamed",
        _ => PlanItem.UsedCollisionSuffix
            ? "Sequenced"
            : "Ready",
    };

    public ImageSource? IconSource { get; }

    public string IconGlyph { get; }

    public Brush PathBrush => DefaultPathBrush;

    public string RenameChevronText => _state switch
    {
        ImageRenameRowState.Completed => "Completed",
        ImageRenameRowState.Renamed => "Renamed",
        _ => "Rename",
    };

    public Brush RenameChevronTrackBrush => _state == ImageRenameRowState.Completed
        ? CompletedChevronTrackBrush
        : PendingChevronTrackBrush;

    public Brush RenameChevronFillBrush => _state == ImageRenameRowState.Completed
        ? CompletedChevronFillBrush
        : PendingChevronFillBrush;

    public double RenameChevronFillWidth => IsCompleted ? ChevronVisualWidth : 0d;

    public Brush RenameChevronTextBrush => _state switch
    {
        ImageRenameRowState.Completed => CompletedChevronTextBrush,
        ImageRenameRowState.Renamed => Brushes.White,
        _ => PendingChevronTextBrush,
    };

    public Brush RenameChevronTipBrush => _state == ImageRenameRowState.Completed
        ? CompletedChevronTipBrush
        : PendingChevronTipBrush;

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

    public void MarkRenamed()
    {
        if (_state != ImageRenameRowState.Pending)
        {
            return;
        }

        var updatedFullPath = BuildUpdatedFullPath(_planItem.SourceFullPath, _planItem.ProposedFileName);
        _planItem = _planItem with
        {
            SourceRelativePath = _planItem.ProposedRelativePath,
            SourceFullPath = updatedFullPath,
            CurrentFileName = _planItem.ProposedFileName,
            IsCompleted = true,
        };

        _state = ImageRenameRowState.Renamed;
        _openPath = updatedFullPath;
        _displayPath = _driveDisplayNameService.FormatPathForDisplay(updatedFullPath);
        _isSelected = false;

        RaisePropertyChanged(nameof(PlanItem));
        RaisePropertyChanged(nameof(ItemKey));
        RaisePropertyChanged(nameof(CurrentFileName));
        RaisePropertyChanged(nameof(DisplayPath));
        RaisePropertyChanged(nameof(OpenPath));
        RaisePropertyChanged(nameof(HasOpenPath));
        RaisePropertyChanged(nameof(IsCompleted));
        RaisePropertyChanged(nameof(IsRenamed));
        RaisePropertyChanged(nameof(CanSelect));
        RaisePropertyChanged(nameof(IsSelected));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(RenameChevronText));
        RaisePropertyChanged(nameof(RenameChevronTrackBrush));
        RaisePropertyChanged(nameof(RenameChevronFillBrush));
        RaisePropertyChanged(nameof(RenameChevronFillWidth));
        RaisePropertyChanged(nameof(RenameChevronTextBrush));
        RaisePropertyChanged(nameof(RenameChevronTipBrush));
    }

    private static string BuildUpdatedFullPath(string sourceFullPath, string proposedFileName)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFullPath);
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? proposedFileName
            : Path.Combine(sourceDirectory, proposedFileName);
    }

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
