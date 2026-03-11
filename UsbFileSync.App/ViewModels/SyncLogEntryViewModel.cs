namespace UsbFileSync.App.ViewModels;

public enum SyncLogSeverity
{
    Verbose,
    Warning,
    Error,
}

public sealed class SyncLogEntryViewModel
{
    private static readonly System.Windows.Media.Brush VerboseBrush = CreateFrozenBrush(32, 32, 32);
    private static readonly System.Windows.Media.Brush AlertBrush = CreateFrozenBrush(192, 0, 0);

    public SyncLogEntryViewModel(string state, string message, SyncLogSeverity severity = SyncLogSeverity.Verbose)
    {
        Timestamp = DateTime.Now;
        State = state;
        Message = message;
        Severity = severity;
    }

    public DateTime Timestamp { get; }

    public string Time => Timestamp.ToString("HH:mm:ss");

    public string State { get; }

    public string Message { get; }

    public SyncLogSeverity Severity { get; }

    public bool IsAlert => Severity != SyncLogSeverity.Verbose;

    public System.Windows.Media.Brush ForegroundBrush => IsAlert ? AlertBrush : VerboseBrush;

    private static System.Windows.Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}