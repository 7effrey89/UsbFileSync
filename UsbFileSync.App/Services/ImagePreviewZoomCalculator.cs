namespace UsbFileSync.App.Services;

public static class ImagePreviewZoomCalculator
{
    public static System.Windows.Size CalculateDisplayedSize(
        double sourceWidth,
        double sourceHeight,
        double viewportWidth,
        double viewportHeight,
        double zoom)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return new System.Windows.Size(0, 0);
        }

        var fitScale = Math.Min(1d, Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight));
        var normalizedZoom = Math.Max(1d, zoom);
        return new System.Windows.Size(sourceWidth * fitScale * normalizedZoom, sourceHeight * fitScale * normalizedZoom);
    }

    public static System.Windows.Point CalculateScrollOffsets(
        double oldContentWidth,
        double oldContentHeight,
        double newContentWidth,
        double newContentHeight,
        double oldHorizontalOffset,
        double oldVerticalOffset,
        double viewportX,
        double viewportY,
        double viewportWidth,
        double viewportHeight)
    {
        if (oldContentWidth <= 0 || oldContentHeight <= 0 || newContentWidth <= 0 || newContentHeight <= 0)
        {
            return new System.Windows.Point(0, 0);
        }

        var relativeX = (oldHorizontalOffset + viewportX) / oldContentWidth;
        var relativeY = (oldVerticalOffset + viewportY) / oldContentHeight;

        var targetHorizontalOffset = (relativeX * newContentWidth) - viewportX;
        var targetVerticalOffset = (relativeY * newContentHeight) - viewportY;

        var maxHorizontalOffset = Math.Max(0, newContentWidth - viewportWidth);
        var maxVerticalOffset = Math.Max(0, newContentHeight - viewportHeight);

        return new System.Windows.Point(
            Math.Clamp(targetHorizontalOffset, 0, maxHorizontalOffset),
            Math.Clamp(targetVerticalOffset, 0, maxVerticalOffset));
    }
}