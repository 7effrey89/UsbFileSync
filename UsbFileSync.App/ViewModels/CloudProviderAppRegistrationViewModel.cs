using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class CloudProviderAppRegistrationViewModel : ObservableObject
{
    private string _registrationId = Guid.NewGuid().ToString("N");
    private CloudStorageProvider _provider;
    private string _alias = string.Empty;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string _tenantId = string.Empty;
    private string _connectionStatusText = string.Empty;

    public CloudProviderAppRegistrationViewModel(CloudStorageProvider provider)
    {
        _provider = provider;
    }

    public string RegistrationId
    {
        get => _registrationId;
        set => SetProperty(ref _registrationId, value);
    }

    public CloudStorageProvider Provider
    {
        get => _provider;
        set
        {
            if (SetProperty(ref _provider, value))
            {
                if (_provider == CloudStorageProvider.OneDrive)
                {
                    TenantId = "common";
                }
                else
                {
                    TenantId = string.Empty;
                }

                RaisePropertyChanged(nameof(ProviderDisplayName));
                RaisePropertyChanged(nameof(UsesTenantId));
                RaisePropertyChanged(nameof(UsesClientSecret));
            }
        }
    }

    public string ProviderDisplayName => CloudStorageProviderInfo.GetDisplayName(Provider);

    public string AccountDisplayName => string.IsNullOrWhiteSpace(Alias)
        ? ProviderDisplayName
        : $"{ProviderDisplayName} - {Alias.Trim()}";

    public bool UsesTenantId => Provider == CloudStorageProvider.OneDrive;

    public bool UsesClientSecret => Provider != CloudStorageProvider.OneDrive;

    public string Alias
    {
        get => _alias;
        set => SetProperty(ref _alias, value);
    }

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string ClientSecret
    {
        get => _clientSecret;
        set => SetProperty(ref _clientSecret, value);
    }

    public string TenantId
    {
        get => _tenantId;
        set => SetProperty(ref _tenantId, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set => SetProperty(ref _connectionStatusText, value);
    }
}
