using UsbFileSync.App.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class DestinationPathEntryViewModel : ObservableObject
{
    private readonly IDriveDisplayNameService _driveDisplayNameService;
    private readonly Action _onChanged;
    private string _path;
    private bool _isFocused;

    public DestinationPathEntryViewModel(
        string path,
        IDriveDisplayNameService driveDisplayNameService,
        Action onChanged)
    {
        _path = path;
        _driveDisplayNameService = driveDisplayNameService;
        _onChanged = onChanged;
    }

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
            {
                RaisePropertyChanged(nameof(DisplayText));
                _onChanged();
            }
        }
    }

    public string DisplayText => _isFocused
        ? Path
        : _driveDisplayNameService.FormatPathForDisplay(Path);

    public void SetFocused(bool isFocused)
    {
        if (SetProperty(ref _isFocused, isFocused))
        {
            RaisePropertyChanged(nameof(DisplayText));
        }
    }
}
