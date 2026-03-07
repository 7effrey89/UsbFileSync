using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Tests;

public sealed class JsonSyncSettingsStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsNull_WhenSettingsFileDoesNotExist()
    {
        var store = new JsonSyncSettingsStore(Path.Combine(_rootPath, "settings.json"));

        var configuration = store.Load();

        Assert.Null(configuration);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsConfiguration()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            Mode = SyncMode.TwoWay,
            DetectMoves = false,
            DryRun = true,
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(configuration.SourcePath, restored.SourcePath);
        Assert.Equal(configuration.DestinationPath, restored.DestinationPath);
        Assert.Equal(configuration.Mode, restored.Mode);
        Assert.Equal(configuration.DetectMoves, restored.DetectMoves);
        Assert.Equal(configuration.DryRun, restored.DryRun);
    }

    [Fact]
    public void Load_ReturnsNull_WhenSettingsFileContainsInvalidJson()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json");
        var store = new JsonSyncSettingsStore(settingsPath);

        var configuration = store.Load();

        Assert.Null(configuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
