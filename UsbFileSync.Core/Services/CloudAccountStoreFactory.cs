namespace UsbFileSync.Core.Services;

public static class CloudAccountStoreFactory
{
    public static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbFileSync",
            "cloud-accounts.json");

    public static ICloudAccountStore CreateDefault() => new JsonCloudAccountStore(GetDefaultPath());
}
