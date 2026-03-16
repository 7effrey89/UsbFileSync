using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class OneDriveAuthenticationServiceTests
{
    [Fact]
    public void ShouldTryConsumersFallback_ReturnsTrue_ForConsumerAudienceCommonError()
    {
        var exception = new InvalidOperationException(
            "The request is not valid for the application's 'userAudience' configuration. In order to use /common/ endpoint, the application must not be configured with 'Consumer' as the user audience.");

        var shouldFallback = OneDriveAuthenticationService.ShouldTryConsumersFallback("common", exception);

        Assert.True(shouldFallback);
    }

    [Fact]
    public void ShouldTryConsumersFallback_ReturnsFalse_ForNonCommonAuthority()
    {
        var exception = new InvalidOperationException(
            "The request is not valid for the application's 'userAudience' configuration. In order to use /common/ endpoint, the application must not be configured with 'Consumer' as the user audience.");

        var shouldFallback = OneDriveAuthenticationService.ShouldTryConsumersFallback("organizations", exception);

        Assert.False(shouldFallback);
    }
}