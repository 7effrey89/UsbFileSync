using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class CloudPathTests
{
    [Fact]
    public void CreateRoot_UsesStableCloudScheme()
    {
        var root = CloudPath.CreateRoot(CloudStorageProvider.GoogleDrive, "account-1");

        Assert.Equal("cloud://googledrive/account-1", root);
    }

    [Fact]
    public void TryParse_ParsesProviderAccountAndRelativePath()
    {
        var success = CloudPath.TryParse(
            "cloud://onedrive/account-123/Documents/Taxes",
            out var provider,
            out var accountId,
            out var relativePath);

        Assert.True(success);
        Assert.Equal(CloudStorageProvider.OneDrive, provider);
        Assert.Equal("account-123", accountId);
        Assert.Equal("Documents/Taxes", relativePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Data")]
    [InlineData("cloud://invalid")]
    [InlineData("cloud://unknown/account")]
    public void TryParse_RejectsInvalidCloudPaths(string path)
    {
        var success = CloudPath.TryParse(path, out _, out _, out _);

        Assert.False(success);
    }
}
