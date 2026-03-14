using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class CloudAccountRegistrationViewModel : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private CloudStorageProvider _provider = CloudStorageProvider.GoogleDrive;
    private string _login = string.Empty;
    private string _localRootPath = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public CloudStorageProvider Provider
    {
        get => _provider;
        set => SetProperty(ref _provider, value);
    }

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public string LocalRootPath
    {
        get => _localRootPath;
        set => SetProperty(ref _localRootPath, value);
    }
}
