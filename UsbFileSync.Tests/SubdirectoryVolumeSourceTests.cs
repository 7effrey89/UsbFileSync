using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Tests;

public sealed class SubdirectoryVolumeSourceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.SubdirectoryVolume", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Enumerate_UsesSubdirectoryAsLogicalRoot()
    {
        var backingRoot = CreateDirectory("source");
        WriteFile(backingRoot, Path.Combine("folder", "nested", "file.txt"), "payload");
        Directory.CreateDirectory(Path.Combine(backingRoot, "folder", "nested", "child"));

        var innerVolume = new WindowsMountedVolume(backingRoot);
        var scopedVolume = new SubdirectoryVolumeSource(innerVolume, "folder/nested");

        var entries = scopedVolume.Enumerate(string.Empty).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(Path.Combine(backingRoot, "folder", "nested"), scopedVolume.Root);
        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal("child", entry.Name);
                Assert.True(entry.IsDirectory);
                Assert.Equal(Path.Combine(backingRoot, "folder", "nested", "child"), entry.FullPath);
            },
            entry =>
            {
                Assert.Equal("file.txt", entry.Name);
                Assert.False(entry.IsDirectory);
                Assert.Equal(Path.Combine(backingRoot, "folder", "nested", "file.txt"), entry.FullPath);
            });
    }

    [Fact]
    public void OpenRead_ReadsFilesRelativeToScopedRoot()
    {
        var backingRoot = CreateDirectory("source");
        WriteFile(backingRoot, Path.Combine("folder", "nested", "file.txt"), "payload");

        var innerVolume = new WindowsMountedVolume(backingRoot);
        var scopedVolume = new SubdirectoryVolumeSource(innerVolume, "folder/nested");

        using var reader = new StreamReader(scopedVolume.OpenRead("file.txt"));

        Assert.Equal("payload", reader.ReadToEnd());
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_rootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}