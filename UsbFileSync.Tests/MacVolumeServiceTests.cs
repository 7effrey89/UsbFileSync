using UsbFileSync.App.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class MacVolumeServiceTests
{
    [Fact]
    public void TryCreateVolume_FallsBackToHfs_WhenApfsDoesNotRecognizeDrive()
    {
        var expectedVolume = new WindowsMountedVolume("C:\\");
        var service = new MacVolumeService(
            new StubSourceVolumeService(null, "The selected drive 'D:\\' does not appear to contain an APFS volume."),
            new StubSourceVolumeService(expectedVolume));

        var success = service.TryCreateVolume("D:\\", out var volume, out var failureReason);

        Assert.True(success);
        Assert.Same(expectedVolume, volume);
        Assert.Null(failureReason);
    }

    [Fact]
    public void CombineFailures_ExplainsNonApfsAndNonHfsVolumes()
    {
        var message = MacVolumeService.CombineFailures(
            "D:\\",
            "The selected drive 'D:\\' does not appear to contain an APFS volume.",
            "The selected drive 'D:\\' does not appear to contain an HFS+ volume.");

        Assert.Equal("The selected drive 'D:\\' does not appear to contain an APFS or HFS+ volume.", message);
    }

    private sealed class StubSourceVolumeService(IVolumeSource? volume, string? failureReason = null) : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out IVolumeSource? resolvedVolume, out string? resolvedFailureReason)
        {
            resolvedVolume = volume;
            resolvedFailureReason = volume is null ? failureReason : null;
            return volume is not null;
        }
    }
}