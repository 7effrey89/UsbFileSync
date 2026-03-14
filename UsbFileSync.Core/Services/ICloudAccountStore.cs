using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

public interface ICloudAccountStore
{
    IReadOnlyList<CloudAccountRegistration> Load();

    void Save(IReadOnlyList<CloudAccountRegistration> accounts);
}
