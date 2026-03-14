using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class CompositeSourceVolumeService : ISourceVolumeService
{
    private readonly IReadOnlyList<ISourceVolumeService> _services;

    public CompositeSourceVolumeService(params ISourceVolumeService[] services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services
            .Where(service => service is not null)
            .ToArray();
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        var failures = new List<string>();
        foreach (var service in _services)
        {
            if (service.TryCreateVolume(path, out volume, out failureReason))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                failures.Add(failureReason);
            }
        }

        failureReason = failures.Count == 0
            ? null
            : string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        return false;
    }
}
