using System.Globalization;
using System.IO;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class SyncPreviewRowViewModel : ObservableObject
{
    private const string RightChevronGeometry = "M0,14 L12,0 L108,0 L120,14 L108,28 L12,28 Z";
    private const string LeftChevronGeometry = "M120,14 L108,0 L12,0 L0,14 L12,28 L108,28 Z";
    private const string RectangleGeometry = "M0,0 L120,0 L120,28 L0,28 Z";

    private static readonly System.Windows.Media.Brush DefaultPathBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly System.Windows.Media.Brush PendingBrush = CreateFrozenBrush(146, 146, 146);
    private static readonly System.Windows.Media.Brush InProgressBrush = CreateFrozenBrush(15, 108, 189);
    private static readonly System.Windows.Media.Brush CompletedBrush = CreateFrozenBrush(24, 142, 76);
    private static readonly System.Windows.Media.Brush PausedBrush = CreateFrozenBrush(214, 140, 0);
    private static readonly System.Windows.Media.Brush SyncActionTrackBrush = CreateFrozenBrush(230, 230, 230);
    private static readonly System.Windows.Media.Brush NewStatusBrush = CreateFrozenBrush(18, 140, 68);
    private static readonly System.Windows.Media.Brush DeletedStatusBrush = CreateFrozenBrush(196, 43, 28);
    private static readonly System.Windows.Media.Brush ModifiedStatusBrush = CreateFrozenBrush(184, 125, 0);
    private static readonly System.Windows.Media.Brush RenamedStatusBrush = CreateFrozenBrush(15, 108, 189);
    private static readonly System.Windows.Media.Brush UnchangedStatusBrush = CreateFrozenBrush(98, 98, 98);

    private readonly SyncActionType? _actionType;
    private readonly bool _hasPlannedAction;
    private bool _isSelected;
    private double _progressValue;
    private PreviewTransferState _progressState;
    private string _transferSpeedText;
    private DateTime? _transferStartedAtUtc;

    public SyncPreviewRowViewModel(SyncPreviewItem item, IFileIconProvider? iconProvider = null)
    {
        Name = GetDisplayPath(item);
        Category = item.Category;
        RelativePath = item.RelativePath;
        Kind = item.IsDirectory ? "Folder" : "File";
        IconSource = (iconProvider ?? ShellFileIconProvider.Instance).GetIcon(Name, item.IsDirectory);
        IconGlyph = item.IsDirectory ? "\uE8B7" : "\uE8A5";
        SourcePath = item.SourceFullPath ?? string.Empty;
        SourceSize = item.IsDirectory ? "Folder" : FormatSize(item.SourceLength);
        SourceModified = FormatTimestamp(item.SourceLastWriteTimeUtc);
        Direction = item.Direction;
        DestinationPath = item.DestinationFullPath ?? string.Empty;
        DestinationSize = item.IsDirectory ? "Folder" : FormatSize(item.DestinationLength);
        DestinationModified = FormatTimestamp(item.DestinationLastWriteTimeUtc);
        Status = item.Status;
        _actionType = item.PlannedActionType;
        Action = item.PlannedActionType?.ToString() ?? "NoAction";
        _hasPlannedAction = item.PlannedActionType.HasValue;
        _progressValue = _hasPlannedAction ? 0 : 100;
        _progressState = _hasPlannedAction ? PreviewTransferState.Pending : PreviewTransferState.Completed;
        _transferSpeedText = _hasPlannedAction ? "Pending" : "On hold";
    }

    public string Name { get; }

    public string OpenPath => Name;

    public string OpenDestinationPath => DestinationPath;

    public string RelativePath { get; }

    public SyncPreviewCategory Category { get; }

    public string Kind { get; }

    public ImageSource? IconSource { get; }

    public string IconGlyph { get; }

    public bool CanSelect => _hasPlannedAction;

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

    public string SourcePath { get; }

    public bool HasSourcePath => !string.IsNullOrWhiteSpace(SourcePath);

    public string SourceSize { get; }

    public string SourceModified { get; }

    public string Direction { get; }

    public string DestinationPath { get; }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    public string DestinationSize { get; }

    public string DestinationModified { get; }

    public string Status { get; }

    public string Action { get; }

    public string SyncActionText
    {
        get
        {
            var actionText = GetActionTextPair();
            return _progressState == PreviewTransferState.Completed
                ? actionText.Completed
                : actionText.Pending;
        }
    }

    public string ActionBadgeText => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => "ADD",
        SyncActionType.CreateDirectoryOnSource => "ADD",
        SyncActionType.CopyToDestination => "ADD",
        SyncActionType.CopyToSource => "ADD",
        SyncActionType.OverwriteFileOnDestination => "OVERWRITE",
        SyncActionType.OverwriteFileOnSource => "OVERWRITE",
        SyncActionType.MoveOnDestination => "MOVE",
        SyncActionType.DeleteDirectoryFromDestination => "DELETE",
        SyncActionType.DeleteDirectoryFromSource => "DELETE",
        SyncActionType.DeleteFromDestination => "DELETE",
        SyncActionType.DeleteFromSource => "DELETE",
        SyncActionType.NoOp => "UNCHANGED",
        null => "UNCHANGED",
        _ => Action.ToUpperInvariant(),
    };

    public System.Windows.Media.Brush ActionBadgeBrush => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => NewStatusBrush,
        SyncActionType.CreateDirectoryOnSource => NewStatusBrush,
        SyncActionType.CopyToDestination => NewStatusBrush,
        SyncActionType.CopyToSource => NewStatusBrush,
        SyncActionType.OverwriteFileOnDestination => ModifiedStatusBrush,
        SyncActionType.OverwriteFileOnSource => ModifiedStatusBrush,
        SyncActionType.MoveOnDestination => RenamedStatusBrush,
        SyncActionType.DeleteDirectoryFromDestination => DeletedStatusBrush,
        SyncActionType.DeleteDirectoryFromSource => DeletedStatusBrush,
        SyncActionType.DeleteFromDestination => DeletedStatusBrush,
        SyncActionType.DeleteFromSource => DeletedStatusBrush,
        SyncActionType.NoOp => UnchangedStatusBrush,
        null => UnchangedStatusBrush,
        _ => UnchangedStatusBrush,
    };

    public System.Windows.Media.Brush ActionBadgeForegroundBrush => GetAccessibleForegroundBrush(ActionBadgeBrush);

    public bool IsSourceAction => _actionType switch
    {
        SyncActionType.CreateDirectoryOnSource => true,
        SyncActionType.CopyToSource => true,
        SyncActionType.OverwriteFileOnSource => true,
        SyncActionType.DeleteDirectoryFromSource => true,
        SyncActionType.DeleteFromSource => true,
        _ => false,
    };

    public bool IsDestinationAction => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => true,
        SyncActionType.CopyToDestination => true,
        SyncActionType.OverwriteFileOnDestination => true,
        SyncActionType.DeleteDirectoryFromDestination => true,
        SyncActionType.DeleteFromDestination => true,
        SyncActionType.MoveOnDestination => true,
        _ => false,
    };

    public string SyncActionShapeData => _actionType switch
    {
        null => RectangleGeometry,
        SyncActionType.NoOp => RectangleGeometry,
        _ when IsSourceAction => LeftChevronGeometry,
        _ => RightChevronGeometry,
    };

    public double SyncActionProgressScale => Math.Clamp(ProgressValue / 100d, 0d, 1d);

    public System.Windows.Media.Brush SyncActionBrush => StatusBrush;

    public System.Windows.Media.Brush SyncActionTrackFillBrush => SyncActionTrackBrush;

    public System.Windows.Media.Brush SourcePathBrush => IsSourceAction ? StatusBrush : DefaultPathBrush;

    public System.Windows.Media.Brush DestinationPathBrush => IsDestinationAction ? StatusBrush : DefaultPathBrush;

    public string SourceStatusGlyph => IsSourceAction ? StatusGlyph : string.Empty;

    public string DestinationStatusGlyph => IsDestinationAction ? StatusGlyph : string.Empty;

    public string StatusGlyph => Status switch
    {
        "New File" => "+",
        "Deleted" => "×",
        "Modified" => "✎",
        "Renamed" => "⇄",
        "Unchanged" => "▮▮",
        _ => "•",
    };

    public System.Windows.Media.Brush StatusBrush => Status switch
    {
        "New File" => NewStatusBrush,
        "Deleted" => DeletedStatusBrush,
        "Modified" => ModifiedStatusBrush,
        "Renamed" => RenamedStatusBrush,
        "Unchanged" => UnchangedStatusBrush,
        _ => UnchangedStatusBrush,
    };

    public System.Windows.Media.Brush PathBrush => StatusBrush;

    public string TransferSpeedText
    {
        get => _transferSpeedText;
        private set => SetProperty(ref _transferSpeedText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set
        {
            var normalizedValue = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _progressValue, normalizedValue))
            {
                RaisePropertyChanged(nameof(ProgressText));
                RaisePropertyChanged(nameof(SyncActionProgressScale));
            }
        }
    }

    public string ProgressText => $"{Math.Round(ProgressValue, 0):0}%";

    public string ProgressStateText => _progressState switch
    {
        PreviewTransferState.Pending => "Queued",
        PreviewTransferState.InProgress => "Transferring",
        PreviewTransferState.Completed => "Done",
        PreviewTransferState.Paused => "Paused",
        _ => "Queued",
    };

    public string ProgressStateGlyph => _progressState switch
    {
        PreviewTransferState.Pending => "○",
        PreviewTransferState.InProgress => "↻",
        PreviewTransferState.Completed => "✔",
        PreviewTransferState.Paused => "⏸",
        _ => "○",
    };

    public System.Windows.Media.Brush ProgressStateBrush => _progressState switch
    {
        PreviewTransferState.Pending => PendingBrush,
        PreviewTransferState.InProgress => InProgressBrush,
        PreviewTransferState.Completed => CompletedBrush,
        PreviewTransferState.Paused => PausedBrush,
        _ => PendingBrush,
    };

    public System.Windows.Media.Brush ProgressStateForegroundBrush => GetAccessibleForegroundBrush(ProgressStateBrush);

    public void MarkInProgress(double progressValue, long bytesTransferred, DateTime nowUtc)
    {
        _transferStartedAtUtc ??= nowUtc;
        ProgressValue = progressValue;
        TransferSpeedText = FormatTransferSpeed(bytesTransferred, nowUtc);
        SetProgressState(PreviewTransferState.InProgress);
    }

    public void MarkCompleted()
    {
        ProgressValue = 100;
        TransferSpeedText = "Done";
        SetProgressState(PreviewTransferState.Completed);
    }

    public void MarkPaused()
    {
        if (!_hasPlannedAction)
        {
            return;
        }

        TransferSpeedText = "Paused";
        SetProgressState(PreviewTransferState.Paused);
    }

    public void MarkPending()
    {
        if (!_hasPlannedAction)
        {
            MarkCompleted();
            return;
        }

        TransferSpeedText = "Pending";
        SetProgressState(PreviewTransferState.Pending);
    }

    private void SetProgressState(PreviewTransferState progressState)
    {
        if (SetProperty(ref _progressState, progressState))
        {
            RaisePropertyChanged(nameof(ProgressStateText));
            RaisePropertyChanged(nameof(ProgressStateGlyph));
            RaisePropertyChanged(nameof(ProgressStateBrush));
            RaisePropertyChanged(nameof(ProgressStateForegroundBrush));
            RaisePropertyChanged(nameof(SyncActionText));
        }
    }

    private (string Pending, string Completed) GetActionTextPair() => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => ("Add", "Added"),
        SyncActionType.CreateDirectoryOnSource => ("Add", "Added"),
        SyncActionType.CopyToDestination => ("Add", "Added"),
        SyncActionType.CopyToSource => ("Add", "Added"),
        SyncActionType.OverwriteFileOnDestination => ("Overwrite", "Overwritten"),
        SyncActionType.OverwriteFileOnSource => ("Overwrite", "Overwritten"),
        SyncActionType.MoveOnDestination => ("Move", "Moved"),
        SyncActionType.DeleteDirectoryFromDestination => ("Delete", "Deleted"),
        SyncActionType.DeleteDirectoryFromSource => ("Delete", "Deleted"),
        SyncActionType.DeleteFromDestination => ("Delete", "Deleted"),
        SyncActionType.DeleteFromSource => ("Delete", "Deleted"),
        SyncActionType.NoOp => ("Unchanged", "Unchanged"),
        null => ("Unchanged", "Unchanged"),
        _ => (Action, Action),
    };

    private static string GetDisplayPath(SyncPreviewItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceFullPath))
        {
            return item.SourceFullPath;
        }

        if (!string.IsNullOrWhiteSpace(item.DestinationFullPath))
        {
            return item.DestinationFullPath;
        }

        return item.RelativePath;
    }

    private static string FormatSize(long? size)
    {
        if (!size.HasValue)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = size.Value;
        var index = 0;
        double displayValue = value;
        while (displayValue >= 1024 && index < units.Length - 1)
        {
            displayValue /= 1024;
            index++;
        }

        return $"{displayValue.ToString(index == 0 ? "0" : "0.##", CultureInfo.InvariantCulture)} {units[index]}";
    }

    private static string FormatTimestamp(DateTime? timestamp) =>
        timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    private string FormatTransferSpeed(long bytesTransferred, DateTime nowUtc)
    {
        if (!_transferStartedAtUtc.HasValue || bytesTransferred <= 0)
        {
            return "0 B/s";
        }

        var elapsedSeconds = Math.Max((nowUtc - _transferStartedAtUtc.Value).TotalSeconds, 0.1d);
        var bytesPerSecond = bytesTransferred / elapsedSeconds;
        return $"{FormatSize((long)Math.Round(bytesPerSecond))}/s";
    }

    private static System.Windows.Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Brush GetAccessibleForegroundBrush(System.Windows.Media.Brush backgroundBrush)
    {
        if (backgroundBrush is not SolidColorBrush solidColorBrush)
        {
            return System.Windows.Media.Brushes.White;
        }

        var luminance =
            (0.2126 * ConvertChannel(solidColorBrush.Color.R)) +
            (0.7152 * ConvertChannel(solidColorBrush.Color.G)) +
            (0.0722 * ConvertChannel(solidColorBrush.Color.B));

        return luminance > 0.35 ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
    }

    private static double ConvertChannel(byte channel)
    {
        var normalizedChannel = channel / 255d;
        return normalizedChannel <= 0.03928d
            ? normalizedChannel / 12.92d
            : Math.Pow((normalizedChannel + 0.055d) / 1.055d, 2.4d);
    }

    private enum PreviewTransferState
    {
        Pending,
        InProgress,
        Completed,
        Paused,
    }
}
