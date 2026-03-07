namespace UsbFileSync.Core.Services;

public sealed class FileChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public FileChangeMonitor(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }

        _watcher = new FileSystemWatcher(sourcePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };

        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public event EventHandler<FileChangeEventArgs>? FileChanged;

    private void OnChanged(object sender, FileSystemEventArgs eventArgs) =>
        FileChanged?.Invoke(this, new FileChangeEventArgs(eventArgs.ChangeType, eventArgs.FullPath, null));

    private void OnRenamed(object sender, RenamedEventArgs eventArgs) =>
        FileChanged?.Invoke(this, new FileChangeEventArgs(eventArgs.ChangeType, eventArgs.FullPath, eventArgs.OldFullPath));

    public void Dispose()
    {
        _watcher.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record FileChangeEventArgs(WatcherChangeTypes ChangeType, string FullPath, string? PreviousFullPath);
