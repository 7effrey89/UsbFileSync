namespace UsbFileSync.App.ViewModels;

public sealed class SelectableTextOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public required string Label { get; init; }

    public required string Value { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
