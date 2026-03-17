using System.Collections.Concurrent;
using System.Diagnostics;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

internal sealed class AnalyzeProgressTracker
{
    private readonly IProgress<AnalyzeProgress> _progress;
    private readonly TimeSpan _minimumInterval;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, long> _pendingByRoot = new(StringComparer.OrdinalIgnoreCase);
    private long _filesScanned;
    private long _directoriesScanned;
    private string _rootPath = string.Empty;
    private string _currentPath = string.Empty;

    public AnalyzeProgressTracker(IProgress<AnalyzeProgress> progress, TimeSpan minimumInterval)
    {
        _progress = progress;
        _minimumInterval = minimumInterval;
    }

    public Action<string, bool, int> CreateObserver(string rootPath)
    {
        _pendingByRoot[rootPath] = 0;

        return (currentPath, isDirectory, pendingDirectories) =>
        {
            _rootPath = rootPath;
            _currentPath = currentPath;
            _pendingByRoot[rootPath] = pendingDirectories;

            if (isDirectory)
            {
                Interlocked.Increment(ref _directoriesScanned);
            }
            else
            {
                Interlocked.Increment(ref _filesScanned);
            }

            if (_stopwatch.Elapsed < _minimumInterval)
            {
                return;
            }

            Flush();
        };
    }

    public void Flush()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            return;
        }

        long totalPending = 0;
        foreach (var pending in _pendingByRoot.Values)
        {
            totalPending += pending;
        }

        _progress.Report(new AnalyzeProgress(_rootPath, _currentPath, _filesScanned, _directoriesScanned, totalPending));
        _stopwatch.Restart();
    }
}