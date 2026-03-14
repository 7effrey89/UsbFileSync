using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class ReadOnlyVolumeExceptionTests
{
    [Fact]
    public void Constructor_UsesCurrentBuildGuidance_ForLinuxVolumes()
    {
        var exception = new ReadOnlyVolumeException("ext4 (D:)");

        Assert.Equal(
            "The volume 'ext4 (D:)' is currently in read-only mode. Write mode for Linux volumes is available only when UsbFileSync is opened with elevated privileges and the drive can be opened through the bundled ext4 writer.",
            exception.Message);
    }

    [Fact]
    public void Constructor_KeepsGenericReadOnlyMessage_ForHfsPlusVolumes()
    {
        var exception = new ReadOnlyVolumeException("HFS+ (F:)");

        Assert.Equal("The volume 'HFS+ (F:)' is read-only.", exception.Message);
    }
}