using UsbFileSync.App.Services;

namespace UsbFileSync.Tests;

public sealed class ExtVolumeServiceTests
{
    [Fact]
    public void SectorAlignedReadStream_ReadsUnalignedRangeFromUnderlyingStream()
    {
        byte[] data = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        using var inner = new MemoryStream(data, writable: false);
        using var stream = new ExtVolumeService.SectorAlignedReadStream(inner, 16);

        stream.Position = 5;
        byte[] buffer = new byte[10];

        int read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(10, read);
        Assert.Equal(data.Skip(5).Take(10).ToArray(), buffer);
        Assert.Equal(15, stream.Position);
    }

    [Fact]
    public void OpenDiskWithRetry_UsesRawDeviceFallback_WhenDiskNumberOpenFails()
    {
        bool repaired = false;
        bool refreshed = false;
        bool delayed = false;

        using var disk = SharpExt4DiskAccessor.OpenDiskWithRetry(
            1,
            log: null,
            maxAttempts: 1,
            delayMilliseconds: 0,
            openByNumber: _ => throw new IOException("Could not read disk MBR."),
            openByDevicePath: static (diskNumber, _, repairMetadata) =>
            {
                var ctor = typeof(SharpExt4.ExtDisk).GetConstructor(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    [typeof(string)],
                    modifiers: null);

                Assert.NotNull(ctor);
                var disk = (SharpExt4.ExtDisk?)ctor!.Invoke(["dummy"]);
                Assert.NotNull(disk);
                repairMetadata(diskNumber, disk!, null);
                return disk;
            },
            repairMetadata: (_, _, _) => repaired = true,
            refreshWindowsStorageCache: _ => refreshed = true,
            delay: _ => delayed = true);

        Assert.True(repaired);
        Assert.False(refreshed);
        Assert.False(delayed);
    }

    [Fact]
    public void OpenWritableFileSystemWithRetry_RetriesTransientPartitionRegistrationFailure()
    {
        var attempts = 0;
        var delays = 0;
        var expectedFileSystem = (SharpExt4.ExtFileSystem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SharpExt4.ExtFileSystem));
        using var disk = (SharpExt4.ExtDisk)typeof(SharpExt4.ExtDisk)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                [typeof(string)],
                modifiers: null)!
            .Invoke(["dummy"]);

        var fileSystem = ExtVolumeService.OpenWritableFileSystemWithRetry(
            disk,
            1,
            openFileSystem: (_, _) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException("Could not register partition.");
                }

                return expectedFileSystem;
            },
            maxAttempts: 3,
            delayMilliseconds: 0,
            delay: _ => delays++);

        Assert.Same(expectedFileSystem, fileSystem);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays);
    }
}