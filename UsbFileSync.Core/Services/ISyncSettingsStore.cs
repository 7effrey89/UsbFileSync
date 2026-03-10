using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

public interface ISyncSettingsStore
{
    SyncConfiguration? Load();

    void Save(SyncConfiguration configuration);
}
