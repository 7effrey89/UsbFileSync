using UsbFileSync.App;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class UniversalSourceLocationPickerDialogTests
{
    [Fact]
    public void BuildBreadcrumbSegments_ReturnsRootOnly_WhenRelativePathIsEmpty()
    {
        var segments = UniversalSourceLocationPickerDialog.BuildBreadcrumbSegments("F:\\", string.Empty);

        var segment = Assert.Single(segments);
        Assert.Equal("F:", segment.DisplayText);
        Assert.Equal(string.Empty, segment.RelativePath);
    }

    [Fact]
    public void BuildBreadcrumbSegments_ReturnsNestedSegments_InOrder()
    {
        var segments = UniversalSourceLocationPickerDialog.BuildBreadcrumbSegments("F:\\", "Photos/2024/Trips");

        Assert.Collection(segments,
            segment =>
            {
                Assert.Equal("F:", segment.DisplayText);
                Assert.Equal(string.Empty, segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("Photos", segment.DisplayText);
                Assert.Equal("Photos", segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("2024", segment.DisplayText);
                Assert.Equal("Photos/2024", segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("Trips", segment.DisplayText);
                Assert.Equal("Photos/2024/Trips", segment.RelativePath);
            });
    }

    [Fact]
    public void TryResolveDialogPath_MapsTypedPathToRootAndRelativePath()
    {
        var success = UniversalSourceLocationPickerDialog.TryResolveDialogPath(["F:\\", "C:\\"], @"F:\\Photos\\2024", out var rootPath, out var relativePath);

        Assert.True(success);
        Assert.Equal("F:\\", rootPath);
        Assert.Equal("Photos/2024", relativePath);
    }

    [Fact]
    public void TryResolveDialogPath_RejectsPathOutsideAvailableRoots()
    {
        var success = UniversalSourceLocationPickerDialog.TryResolveDialogPath(["F:\\"], @"C:\\Work", out _, out _);

        Assert.False(success);
    }

    [Fact]
    public void BuildBreadcrumbSegments_UsesGoogleDriveDisplayName_ForGoogleDriveRoot()
    {
        var segments = UniversalSourceLocationPickerDialog.BuildBreadcrumbSegments(GoogleDrivePath.RootPath, "Photos/2024");

        Assert.Collection(segments,
            segment =>
            {
                Assert.Equal("Google Drive", segment.DisplayText);
                Assert.Equal(string.Empty, segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("Photos", segment.DisplayText);
                Assert.Equal("Photos", segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("2024", segment.DisplayText);
                Assert.Equal("Photos/2024", segment.RelativePath);
            });
    }

    [Fact]
    public void TryResolveDialogPath_MapsGoogleDrivePathToRootAndRelativePath()
    {
        var success = UniversalSourceLocationPickerDialog.TryResolveDialogPath([GoogleDrivePath.RootPath], "gdrive://root/Photos/2024", out var rootPath, out var relativePath);

        Assert.True(success);
        Assert.Equal(GoogleDrivePath.RootPath, rootPath);
        Assert.Equal("Photos/2024", relativePath);
    }

    [Fact]
    public void BuildBreadcrumbSegments_UsesOneDriveDisplayName_ForOneDriveRoot()
    {
        var segments = UniversalSourceLocationPickerDialog.BuildBreadcrumbSegments(OneDrivePath.RootPath, "Projects/2026");

        Assert.Collection(segments,
            segment =>
            {
                Assert.Equal("OneDrive", segment.DisplayText);
                Assert.Equal(string.Empty, segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("Projects", segment.DisplayText);
                Assert.Equal("Projects", segment.RelativePath);
            },
            segment =>
            {
                Assert.Equal("2026", segment.DisplayText);
                Assert.Equal("Projects/2026", segment.RelativePath);
            });
    }

    [Fact]
    public void TryResolveDialogPath_MapsOneDrivePathToRootAndRelativePath()
    {
        var success = UniversalSourceLocationPickerDialog.TryResolveDialogPath([OneDrivePath.RootPath], "onedrive://root/Projects/2026", out var rootPath, out var relativePath);

        Assert.True(success);
        Assert.Equal(OneDrivePath.RootPath, rootPath);
        Assert.Equal("Projects/2026", relativePath);
    }

    [Theory]
    [InlineData("New Folder", "New Folder")]
    [InlineData("  Trimmed  ", "Trimmed")]
    public void TryNormalizeNewFolderName_AcceptsValidNames(string input, string expected)
    {
        var success = UniversalSourceLocationPickerDialog.TryNormalizeNewFolderName(input, out var normalizedFolderName, out var errorMessage);

        Assert.True(success);
        Assert.Equal(expected, normalizedFolderName);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Folder/Sub")]
    [InlineData("Folder\\Sub")]
    [InlineData("Folder*")]
    public void TryNormalizeNewFolderName_RejectsInvalidNames(string input)
    {
        var success = UniversalSourceLocationPickerDialog.TryNormalizeNewFolderName(input, out _, out var errorMessage);

        Assert.False(success);
        Assert.NotEqual(string.Empty, errorMessage);
    }
}