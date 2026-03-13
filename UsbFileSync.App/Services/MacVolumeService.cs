using UsbFileSync.Core.Volumes;

namespace UsbFileSync.App.Services;

public sealed class MacVolumeService : ISourceVolumeService
{
    private readonly ISourceVolumeService _apfsVolumeService;
    private readonly ISourceVolumeService _hfsVolumeService;

    public MacVolumeService()
        : this(new ParagonApfsVolumeService(), new HfsPlusVolumeService())
    {
    }

    internal MacVolumeService(ISourceVolumeService apfsVolumeService, ISourceVolumeService hfsVolumeService)
    {
        _apfsVolumeService = apfsVolumeService;
        _hfsVolumeService = hfsVolumeService;
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        if (_apfsVolumeService.TryCreateVolume(path, out volume, out failureReason))
        {
            return true;
        }

        var apfsFailureReason = failureReason;
        if (_hfsVolumeService.TryCreateVolume(path, out volume, out failureReason))
        {
            return true;
        }

        failureReason = CombineFailures(path, apfsFailureReason, failureReason);
        return false;
    }

    internal static string CombineFailures(string path, string? apfsFailureReason, string? hfsFailureReason)
    {
        if (ParagonApfsVolumeService.IsNotApfsFailure(apfsFailureReason)
            && HfsPlusVolumeService.IsNotHfsFailure(hfsFailureReason))
        {
            return $"The selected drive '{path}' does not appear to contain an APFS or HFS+ volume.";
        }

        if (string.Equals(apfsFailureReason, hfsFailureReason, StringComparison.Ordinal))
        {
            return apfsFailureReason ?? hfsFailureReason ?? "The selected drive could not be opened.";
        }

        if (string.IsNullOrWhiteSpace(apfsFailureReason))
        {
            return hfsFailureReason ?? "The selected drive could not be opened.";
        }

        if (string.IsNullOrWhiteSpace(hfsFailureReason))
        {
            return apfsFailureReason;
        }

        return $"{apfsFailureReason} {hfsFailureReason}";
    }
}