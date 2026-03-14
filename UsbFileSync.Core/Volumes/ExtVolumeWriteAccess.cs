using System.Security.Principal;

namespace UsbFileSync.Core.Volumes;

internal static class ExtVolumeWriteAccess
{
    public static bool HasRequiredWriteAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}