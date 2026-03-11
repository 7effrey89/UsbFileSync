using System.Windows;
using UsbFileSync.App.Services;

namespace UsbFileSync.Tests;

public sealed class ImagePreviewZoomCalculatorTests
{
    [Fact]
    public void CalculateDisplayedSize_ScalesLargeImageToFitViewport()
    {
        var displayedSize = ImagePreviewZoomCalculator.CalculateDisplayedSize(4000, 2000, 500, 300, 1d);

        Assert.Equal(500, displayedSize.Width, 3);
        Assert.Equal(250, displayedSize.Height, 3);
    }

    [Fact]
    public void CalculateDisplayedSize_PreservesSmallImageSizeAtBaseZoom()
    {
        var displayedSize = ImagePreviewZoomCalculator.CalculateDisplayedSize(200, 100, 500, 300, 1d);

        Assert.Equal(200, displayedSize.Width, 3);
        Assert.Equal(100, displayedSize.Height, 3);
    }

    [Fact]
    public void CalculateScrollOffsets_KeepsClickedPointStableAcrossZoom()
    {
        var offsets = ImagePreviewZoomCalculator.CalculateScrollOffsets(
            500,
            250,
            625,
            312.5,
            0,
            0,
            250,
            125,
            500,
            300);

        Assert.Equal(62.5, offsets.X, 3);
        Assert.Equal(6.25, offsets.Y, 3);
    }
}