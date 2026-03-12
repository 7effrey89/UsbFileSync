namespace UsbFileSync.App.ViewModels;

public enum PreviewColumnKey
{
    SourceFile,
    SyncAction,
    TransferSpeed,
    DriveLocation,
    DestinationFile,
    FileType,
    SourceSize,
    DestinationSize,
    SourceModified,
    DestinationModified,
    Status,
    Action,
}

public sealed class PreviewColumnHeader
{
    public required string Title { get; init; }

    public PreviewColumnKey ColumnKey { get; init; }
}

public sealed class PreviewFilterOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public PreviewFilterOptionViewModel(string value, bool isSelected)
    {
        Value = value;
        _isSelected = isSelected;
    }

    public string Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}