namespace UsbFileSync.App.ViewModels;

public sealed class EditableSelectableTextOptionViewModel : ObservableObject
{
    private string _value = string.Empty;
    private bool _isSelected = true;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
