using UsbFileSync.App.Services;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Tests;

public sealed class WindowsSourceLocationPickerServiceTests
{
    [Fact]
    public void GetDestinationBrowseVolumeService_UsesReadOnlyExtProbe_ForExtDestinationService()
    {
        var originalService = new ExtVolumeService(allowWriteAccess: true);
        var replacementService = new StubSourceVolumeService();

        var browseService = WindowsSourceLocationPickerService.GetDestinationBrowseVolumeService(
            originalService,
            () => replacementService);

        Assert.Same(replacementService, browseService);
        Assert.NotSame(originalService, browseService);
    }

    [Fact]
    public void GetDestinationBrowseVolumeService_KeepsCustomDestinationService_WhenNotExtVolumeService()
    {
        var originalService = new StubSourceVolumeService();
        var replacementService = new StubSourceVolumeService();

        var browseService = WindowsSourceLocationPickerService.GetDestinationBrowseVolumeService(
            originalService,
            () => replacementService);

        Assert.Same(originalService, browseService);
    }

    private sealed class StubSourceVolumeService : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out UsbFileSync.Core.Volumes.IVolumeSource? volume, out string? failureReason)
        {
            volume = null;
            failureReason = null;
            return false;
        }
    }
}