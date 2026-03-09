namespace UsbFileSync.App.ViewModels;

public sealed class SyncLogEntryViewModel
{
    public SyncLogEntryViewModel(string state, string message)
    {
        Timestamp = DateTime.Now;
        State = state;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public string Time => Timestamp.ToString("HH:mm:ss");

    public string State { get; }

    public string Message { get; }
}