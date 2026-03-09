using UsbFileSync.App;

namespace UsbFileSync.Tests;

public sealed class SettingsDialogTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData("32", 32)]
    public void TryParseParallelCopyCount_AcceptsZeroAndPositiveValues(string text, int expected)
    {
        var success = SettingsDialog.TryParseParallelCopyCount(text, out var value);

        Assert.True(success);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void TryParseParallelCopyCount_RejectsInvalidValues(string text)
    {
        var success = SettingsDialog.TryParseParallelCopyCount(text, out _);

        Assert.False(success);
    }
}