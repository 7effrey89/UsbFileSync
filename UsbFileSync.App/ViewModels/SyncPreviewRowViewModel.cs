using System.Globalization;
using System.IO;
using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class SyncPreviewRowViewModel : ObservableObject
{
    private static readonly System.Windows.Media.Brush PendingBrush = System.Windows.Media.Brushes.DarkGray;
    private static readonly System.Windows.Media.Brush InProgressBrush = System.Windows.Media.Brushes.DarkOrange;
    private static readonly System.Windows.Media.Brush CompletedBrush = System.Windows.Media.Brushes.ForestGreen;
    private static readonly System.Windows.Media.Brush PausedBrush = System.Windows.Media.Brushes.Goldenrod;
    private static readonly System.Windows.Media.Brush NewStatusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 140, 68));
    private static readonly System.Windows.Media.Brush DeletedStatusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(196, 43, 28));
    private static readonly System.Windows.Media.Brush ModifiedStatusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 125, 0));
    private static readonly System.Windows.Media.Brush UnchangedStatusBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(98, 98, 98));

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

    public string SourceSize { get; }

    public string SourceModified { get; }

    public string Direction { get; }

    public string DestinationPath { get; }

    public string DestinationSize { get; }

    public string DestinationModified { get; }

    public string Status { get; }

    public string Action { get; }

    public string StatusGlyph => Status switch
    {
        "New File" => "+",
        "Deleted" => "×",
        "Modified" => "✎",
        "Unchanged" => "▮▮",
        _ => "•",
    };

    public System.Windows.Media.Brush StatusBrush => Status switch
    {
        "New File" => NewStatusBrush,
        "Deleted" => DeletedStatusBrush,
        "Modified" => ModifiedStatusBrush,
        "Unchanged" => UnchangedStatusBrush,
        _ => UnchangedStatusBrush,
    };

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
        }
    }

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

    private enum PreviewTransferState
    {
        Pending,
        InProgress,
        Completed,
        Paused,
    }
}