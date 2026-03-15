using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class CloudProviderAppRegistrationViewModel : ObservableObject
{
    private string _clientId = string.Empty;
    private string _tenantId = string.Empty;

    public CloudProviderAppRegistrationViewModel(CloudStorageProvider provider)
    {
        Provider = provider;
    }

    public CloudStorageProvider Provider { get; }

    public string ProviderDisplayName => CloudStorageProviderInfo.GetDisplayName(Provider);

    public bool UsesTenantId => Provider == CloudStorageProvider.OneDrive;

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string TenantId
    {
        get => _tenantId;
        set => SetProperty(ref _tenantId, value);
    }
}
