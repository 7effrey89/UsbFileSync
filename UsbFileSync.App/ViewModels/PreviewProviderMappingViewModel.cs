using UsbFileSync.App.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class PreviewProviderMappingViewModel : ObservableObject
{
    private string _extension = string.Empty;
    private PreviewProviderKind _providerKind;

    public string Extension
    {
        get => _extension;
        set => SetProperty(ref _extension, value);
    }

    public PreviewProviderKind ProviderKind
    {
        get => _providerKind;
        set => SetProperty(ref _providerKind, value);
    }
}