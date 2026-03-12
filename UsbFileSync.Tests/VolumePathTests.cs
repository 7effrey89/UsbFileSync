using System.Runtime.InteropServices;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class VolumePathTests
{
    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("folder/file.txt", "file.txt")]
    [InlineData("folder\\nested\\file.txt", "file.txt")]
    [InlineData("", "")]
    [InlineData("/", "")]
    public void GetName_ReturnsExpectedFileName(string path, string expected)
    {
        Assert.Equal(expected, VolumePath.GetName(path));
    }

    [Theory]
    [InlineData("C:\\", true)]
    [InlineData("D:\\Backup", true)]
    [InlineData("\\\\server\\share", true)]
    [InlineData("ext4://disk1", false)]
    [InlineData("apfs://disk2", false)]
    public void LooksLikeWindowsPath_DetectsExpectedFormats(string path, bool expected)
    {
        Assert.Equal(expected, VolumePath.LooksLikeWindowsPath(path));
    }

    [Fact]
    public void LooksLikeWindowsPath_EmptyPathFallsBackToCurrentPlatform()
    {
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), VolumePath.LooksLikeWindowsPath(string.Empty));
    }

    [Fact]
    public void TemporaryCopyPathBuilder_UsesRootDirectoryFileNameWhenFileIsAtRoot()
    {
        var tempPath = TemporaryCopyPathBuilder.Build("file.txt");

        Assert.Matches("^\\.file\\.txt\\.[0-9a-f]{32}\\.usfcopy\\.tmp$", tempPath);
    }

    [Fact]
    public void TemporaryCopyPathBuilder_PreservesNestedDirectoryStructure()
    {
        var tempPath = TemporaryCopyPathBuilder.Build("folder/sub/file.txt");

        Assert.Matches("^folder/sub/\\.file\\.txt\\.[0-9a-f]{32}\\.usfcopy\\.tmp$", tempPath);
    }
}
