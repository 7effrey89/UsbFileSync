using System.Windows.Media;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Tests;

public sealed class ImageRenameRowViewModelTests
{
    [Fact]
    public void PendingChevronUsesSyncAddPalette()
    {
        var row = CreateRow(isCompleted: false);

        AssertBrushColor(row.RenameChevronTrackBrush, 198, 239, 206);
        AssertBrushColor(row.RenameChevronFillBrush, 78, 167, 46);
        AssertBrushColor(row.RenameChevronTextBrush, 0, 97, 0);
        AssertBrushColor(row.RenameChevronTipBrush, 56, 118, 29);
        Assert.Equal(0d, row.RenameChevronFillWidth);
        Assert.Equal("Rename", row.RenameChevronText);
    }

    [Fact]
    public void CompletedChevronRemainsFullyFilledAndNeutral()
    {
        var row = CreateRow(isCompleted: true);

        AssertBrushColor(row.RenameChevronTrackBrush, 228, 228, 228);
        AssertBrushColor(row.RenameChevronFillBrush, 146, 146, 146);
        AssertBrushColor(row.RenameChevronTextBrush, 255, 255, 255);
        AssertBrushColor(row.RenameChevronTipBrush, 98, 98, 98);
        Assert.Equal(120d, row.RenameChevronFillWidth);
        Assert.Equal("Completed", row.RenameChevronText);
    }

    [Fact]
    public void MarkRenamed_UsesSyncAddedCompletedPalette()
    {
        var row = CreateRow(isCompleted: false);

        row.MarkRenamed();

        AssertBrushColor(row.RenameChevronTrackBrush, 198, 239, 206);
        AssertBrushColor(row.RenameChevronFillBrush, 78, 167, 46);
        AssertBrushColor(row.RenameChevronTextBrush, 255, 255, 255);
        AssertBrushColor(row.RenameChevronTipBrush, 56, 118, 29);
        Assert.Equal(120d, row.RenameChevronFillWidth);
        Assert.Equal("Renamed", row.RenameChevronText);
        Assert.Equal("Renamed", row.StatusText);
        Assert.False(row.CanSelect);
        Assert.True(row.IsCompleted);
        Assert.True(row.IsRenamed);
    }

    private static ImageRenameRowViewModel CreateRow(bool isCompleted)
    {
        var planItem = new ImageRenamePlanItem(
            SourceRelativePath: "folder/photo.jpg",
            SourceFullPath: @"F:\folder\photo.jpg",
            CurrentFileName: "photo.jpg",
            ProposedFileName: "2026-03-19 120000.jpg",
            ProposedRelativePath: "folder/2026-03-19 120000.jpg",
            MatchedFileNameMask: "IMG_*",
            TimestampLocal: new DateTime(2026, 3, 19, 12, 0, 0),
            UsedCollisionSuffix: false,
            IsMatchedByFileNameMask: true,
            IsCompleted: isCompleted);

        return new ImageRenameRowViewModel(planItem);
    }

    private static void AssertBrushColor(Brush brush, byte red, byte green, byte blue)
    {
        var solidColorBrush = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(red, solidColorBrush.Color.R);
        Assert.Equal(green, solidColorBrush.Color.G);
        Assert.Equal(blue, solidColorBrush.Color.B);
        Assert.Equal(255, solidColorBrush.Color.A);
    }
}