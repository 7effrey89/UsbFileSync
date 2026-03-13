using UsbFileSync.App.Services;

namespace UsbFileSync.Tests;

public sealed class ParagonApfsVolumeServiceTests
{
    [Fact]
    public void NormalizeProbeFailure_ExplainsDriveIsNotApfs()
    {
        var message = ParagonApfsVolumeService.NormalizeProbeFailure(
            "UFSD::Init returns a0001006 \"apfsutil\": Unknown FileSystem",
            "D:\\");

        Assert.Equal(
                "The selected drive 'D:\\' does not appear to contain an APFS volume.",
            message);
    }

    [Fact]
    public void NormalizeProbeFailure_ExplainsMissingDrive()
    {
        var message = ParagonApfsVolumeService.NormalizeProbeFailure(
            "\"apfsutil\": Error 0x2",
            "D:\\");

        Assert.Equal(
            "The selected drive 'D:\\' is not currently available. Reconnect the drive and try again.",
            message);
    }
}