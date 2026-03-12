using System.Globalization;
using System.IO;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class SyncPreviewRowViewModel : ObservableObject
{
    private const string RightChevronGeometry = "M0,2 L108,2 L120,14 L108,26 L0,26 L12,14 Z";
    private const string LeftChevronGeometry = "M120,2 L12,2 L0,14 L12,26 L120,26 L108,14 Z";
    private const string RightChevronTipBorderGeometry = "M108,2 L120,14 L108,26";
    private const string LeftChevronTipBorderGeometry = "M12,2 L0,14 L12,26";
    private const string RectangleGeometry = "M0,0 L120,0 L120,28 L0,28 Z";
    private const double SyncActionVisualWidth = 120d;

    private static readonly Geometry RightChevronGeometryShape = CreateFrozenGeometry(RightChevronGeometry);
    private static readonly Geometry LeftChevronGeometryShape = CreateFrozenGeometry(LeftChevronGeometry);
    private static readonly Geometry RightChevronTipBorderGeometryShape = CreateFrozenGeometry(RightChevronTipBorderGeometry);
    private static readonly Geometry LeftChevronTipBorderGeometryShape = CreateFrozenGeometry(LeftChevronTipBorderGeometry);
    private static readonly Geometry RectangleGeometryShape = CreateFrozenGeometry(RectangleGeometry);

    private static readonly System.Windows.Media.Brush DefaultPathBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly System.Windows.Media.Brush PendingBrush = CreateFrozenBrush(146, 146, 146);
    private static readonly System.Windows.Media.Brush InProgressBrush = CreateFrozenBrush(15, 108, 189);
    private static readonly System.Windows.Media.Brush CompletedBrush = CreateFrozenBrush(24, 142, 76);
    private static readonly System.Windows.Media.Brush PausedBrush = CreateFrozenBrush(214, 140, 0);
    private static readonly System.Windows.Media.Brush NeutralSyncActionTrackBrush = CreateFrozenBrush(230, 230, 230);
    private static readonly System.Windows.Media.Brush NewStatusBrush = CreateFrozenBrush(18, 140, 68);
    private static readonly System.Windows.Media.Brush DeletedStatusBrush = CreateFrozenBrush(196, 43, 28);
    private static readonly System.Windows.Media.Brush ModifiedStatusBrush = CreateFrozenBrush(184, 125, 0);
    private static readonly System.Windows.Media.Brush RenamedStatusBrush = CreateFrozenBrush(15, 108, 189);
    private static readonly System.Windows.Media.Brush UnchangedStatusBrush = CreateFrozenBrush(98, 98, 98);
    private static readonly System.Windows.Media.Brush NewActionTrackBrush = CreateFrozenBrush(198, 239, 206);
    private static readonly System.Windows.Media.Brush NewActionFillBrush = CreateFrozenBrush(78, 167, 46);
    private static readonly System.Windows.Media.Brush NewActionTextBrush = CreateFrozenBrush(0, 97, 0);
    private static readonly System.Windows.Media.Brush NewActionTipBrush = CreateFrozenBrush(56, 118, 29);
    private static readonly System.Windows.Media.Brush ModifiedActionTrackBrush = CreateFrozenBrush(249, 232, 158);
    private static readonly System.Windows.Media.Brush ModifiedActionFillBrush = CreateFrozenBrush(240, 198, 78);
    private static readonly System.Windows.Media.Brush ModifiedActionCompletedFillBrush = CreateFrozenBrush(255, 192, 0);
    private static readonly System.Windows.Media.Brush ModifiedActionTipBrush = CreateFrozenBrush(191, 144, 0);
    private static readonly System.Windows.Media.Brush DeletedActionTrackBrush = CreateFrozenBrush(246, 206, 206);
    private static readonly System.Windows.Media.Brush DeletedActionFillBrush = CreateFrozenBrush(192, 0, 0);
    private static readonly System.Windows.Media.Brush DeletedActionTipBrush = CreateFrozenBrush(128, 0, 0);
    private static readonly System.Windows.Media.Brush RenamedActionTrackBrush = CreateFrozenBrush(196, 225, 248);
    private static readonly System.Windows.Media.Brush RenamedActionFillBrush = CreateFrozenBrush(121, 180, 230);
    private static readonly System.Windows.Media.Brush RenamedActionTipBrush = CreateFrozenBrush(47, 117, 181);
    private static readonly System.Windows.Media.Brush TransparentBrush = CreateFrozenBrush(0, 0, 0, 0);

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
        FileType = GetFileType(item);
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

    public string FileType { get; }

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

    public string SyncActionDisplayText => SyncActionText.ToUpperInvariant();

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

    public bool IsUnchangedAction => _actionType is null or SyncActionType.NoOp;

    public Geometry SyncActionGeometry => _actionType switch
    {
        null => RectangleGeometryShape,
        SyncActionType.NoOp => RectangleGeometryShape,
        _ when IsSourceAction => LeftChevronGeometryShape,
        _ => RightChevronGeometryShape,
    };

    public string SyncActionPathData => SyncActionGeometry.ToString(CultureInfo.InvariantCulture);

    public Geometry SyncActionTipBorderGeometry => _actionType switch
    {
        null => RectangleGeometryShape,
        SyncActionType.NoOp => RectangleGeometryShape,
        _ when IsSourceAction => LeftChevronTipBorderGeometryShape,
        _ => RightChevronTipBorderGeometryShape,
    };

    public string SyncActionTipBorderPathData => SyncActionTipBorderGeometry.ToString(CultureInfo.InvariantCulture);

    public double SyncActionProgressScale => Math.Clamp(ProgressValue / 100d, 0d, 1d);

    public double SyncActionFillWidth => SyncActionProgressScale * SyncActionVisualWidth;

    public System.Windows.Media.Brush SyncActionBrush => IsUnchangedAction ? TransparentBrush : SyncActionFillBrush;

    public System.Windows.Media.Brush SyncActionTrackFillBrush => IsUnchangedAction ? TransparentBrush : SyncActionTrackBrush;

    public System.Windows.Media.Brush SyncActionStrokeBrush => TransparentBrush;

    public System.Windows.Media.Brush SyncActionTipBrush => IsUnchangedAction ? TransparentBrush : SyncActionTipStrokeBrush;

    public System.Windows.Media.Brush SyncActionTextBrush => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.CreateDirectoryOnSource when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.CopyToDestination when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.CopyToSource when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.OverwriteFileOnDestination when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.Black,
        SyncActionType.OverwriteFileOnSource when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.Black,
        SyncActionType.DeleteDirectoryFromDestination when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.DeleteDirectoryFromSource when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.DeleteFromDestination when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.DeleteFromSource when _progressState == PreviewTransferState.Completed => System.Windows.Media.Brushes.White,
        SyncActionType.CreateDirectoryOnDestination => NewActionTextBrush,
        SyncActionType.CreateDirectoryOnSource => NewActionTextBrush,
        SyncActionType.CopyToDestination => NewActionTextBrush,
        SyncActionType.CopyToSource => NewActionTextBrush,
        _ when IsUnchangedAction => DefaultPathBrush,
        _ => StatusBrush,
    };

    public System.Windows.Media.Brush SourcePathBrush => IsSourceAction ? StatusBrush : DefaultPathBrush;

    public System.Windows.Media.Brush DestinationPathBrush => IsDestinationAction ? StatusBrush : DefaultPathBrush;

    public string SourceStatusGlyph => HasSourcePath && (IsSourceAction || IsUnchangedAction) ? StatusGlyph : string.Empty;

    public string DestinationStatusGlyph => HasDestinationPath && (IsDestinationAction || IsUnchangedAction) ? StatusGlyph : string.Empty;

    public System.Windows.Media.Brush SourceStatusGlyphBrush => IsSourceAction ? SourcePathBrush : StatusBrush;

    public System.Windows.Media.Brush DestinationStatusGlyphBrush => IsDestinationAction ? DestinationPathBrush : StatusBrush;

    public string StatusGlyph => Status switch
    {
        "New File" => "+",
        "Deleted" => "×",
        "Modified" => "✎",
        "Renamed" => "⇄",
        "Unchanged" => "||",
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

    private System.Windows.Media.Brush SyncActionTrackBrush => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => NewActionTrackBrush,
        SyncActionType.CreateDirectoryOnSource => NewActionTrackBrush,
        SyncActionType.CopyToDestination => NewActionTrackBrush,
        SyncActionType.CopyToSource => NewActionTrackBrush,
        SyncActionType.OverwriteFileOnDestination => ModifiedActionTrackBrush,
        SyncActionType.OverwriteFileOnSource => ModifiedActionTrackBrush,
        SyncActionType.MoveOnDestination => RenamedActionTrackBrush,
        SyncActionType.DeleteDirectoryFromDestination => DeletedActionTrackBrush,
        SyncActionType.DeleteDirectoryFromSource => DeletedActionTrackBrush,
        SyncActionType.DeleteFromDestination => DeletedActionTrackBrush,
        SyncActionType.DeleteFromSource => DeletedActionTrackBrush,
        _ => NeutralSyncActionTrackBrush,
    };

    private System.Windows.Media.Brush SyncActionFillBrush => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => NewActionFillBrush,
        SyncActionType.CreateDirectoryOnSource => NewActionFillBrush,
        SyncActionType.CopyToDestination => NewActionFillBrush,
        SyncActionType.CopyToSource => NewActionFillBrush,
        SyncActionType.OverwriteFileOnDestination when _progressState == PreviewTransferState.Completed => ModifiedActionCompletedFillBrush,
        SyncActionType.OverwriteFileOnSource when _progressState == PreviewTransferState.Completed => ModifiedActionCompletedFillBrush,
        SyncActionType.OverwriteFileOnDestination => ModifiedActionFillBrush,
        SyncActionType.OverwriteFileOnSource => ModifiedActionFillBrush,
        SyncActionType.MoveOnDestination => RenamedActionFillBrush,
        SyncActionType.DeleteDirectoryFromDestination => DeletedActionFillBrush,
        SyncActionType.DeleteDirectoryFromSource => DeletedActionFillBrush,
        SyncActionType.DeleteFromDestination => DeletedActionFillBrush,
        SyncActionType.DeleteFromSource => DeletedActionFillBrush,
        _ => TransparentBrush,
    };

    private System.Windows.Media.Brush SyncActionTipStrokeBrush => _actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => NewActionTipBrush,
        SyncActionType.CreateDirectoryOnSource => NewActionTipBrush,
        SyncActionType.CopyToDestination => NewActionTipBrush,
        SyncActionType.CopyToSource => NewActionTipBrush,
        SyncActionType.OverwriteFileOnDestination => ModifiedActionTipBrush,
        SyncActionType.OverwriteFileOnSource => ModifiedActionTipBrush,
        SyncActionType.MoveOnDestination => RenamedActionTipBrush,
        SyncActionType.DeleteDirectoryFromDestination => DeletedActionTipBrush,
        SyncActionType.DeleteDirectoryFromSource => DeletedActionTipBrush,
        SyncActionType.DeleteFromDestination => DeletedActionTipBrush,
        SyncActionType.DeleteFromSource => DeletedActionTipBrush,
        _ => TransparentBrush,
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
                RaisePropertyChanged(nameof(SyncActionFillWidth));
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
            RaisePropertyChanged(nameof(SyncActionDisplayText));
            RaisePropertyChanged(nameof(SyncActionBrush));
            RaisePropertyChanged(nameof(SyncActionTrackFillBrush));
            RaisePropertyChanged(nameof(SyncActionStrokeBrush));
            RaisePropertyChanged(nameof(SyncActionTipBrush));
            RaisePropertyChanged(nameof(SyncActionTextBrush));
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

    private static string GetFileType(SyncPreviewItem item)
    {
        if (item.IsDirectory)
        {
            return "Folder";
        }

        var path = !string.IsNullOrWhiteSpace(item.SourceFullPath)
            ? item.SourceFullPath
            : !string.IsNullOrWhiteSpace(item.DestinationFullPath)
                ? item.DestinationFullPath
                : item.RelativePath;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File";
        }

        return extension.TrimStart('.').ToUpperInvariant();
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
        return CreateFrozenBrush(255, red, green, blue);
    }

    private static System.Windows.Media.Brush CreateFrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Geometry CreateFrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
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
