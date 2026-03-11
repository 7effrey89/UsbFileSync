using UsbFileSync.App;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;

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

    [Fact]
    public void TryCreateSerializableMappings_NormalizesExtensionsAndSerializesProviders()
    {
        var mappings = new[]
        {
            new PreviewProviderMappingViewModel { Extension = "txt", ProviderKind = PreviewProviderKind.Text },
            new PreviewProviderMappingViewModel { Extension = ".pdf", ProviderKind = PreviewProviderKind.Pdf },
        };

        var success = SettingsDialog.TryCreateSerializableMappings(mappings, out var serializedMappings, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal("Text", serializedMappings[".txt"]);
        Assert.Equal("Pdf", serializedMappings[".pdf"]);
    }

    [Fact]
    public void TryCreateSerializableMappings_RejectsDuplicateExtensions()
    {
        var mappings = new[]
        {
            new PreviewProviderMappingViewModel { Extension = ".txt", ProviderKind = PreviewProviderKind.Text },
            new PreviewProviderMappingViewModel { Extension = "txt", ProviderKind = PreviewProviderKind.Unsupported },
        };

        var success = SettingsDialog.TryCreateSerializableMappings(mappings, out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("listed more than once", errorMessage);
    }
}