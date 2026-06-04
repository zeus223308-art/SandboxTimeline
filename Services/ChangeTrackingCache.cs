using System.Collections.Concurrent;

namespace SandboxTimeline;

public sealed class ChangeTrackingCache : IDisposable
{
    private readonly ConcurrentDictionary<string, TrackedFileEntry> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pollLock = new();
    private readonly DifferentialFileTracker _tracker = new();
    private System.Threading.Timer? _pollTimer;
    private long _lastPolledUsn;
    private IReadOnlyList<string> _watchRoots = [];
    private bool _started;

    public int PendingCount => _pendingChanges.Count;

    public void Start()
    {
        lock (_pollLock)
        {
            if (_started)
            {
                return;
            }

            _watchRoots = _tracker.GetWatchRoots();
            var checkpoint = UsnChangeReader.QueryCurrentCheckpoint();
            _lastPolledUsn = checkpoint.NextUsn;
            _started = true;

            _pollTimer = new System.Threading.Timer(_ => PollUsnDelta(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        }
    }

    public void Stop()
    {
        lock (_pollLock)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _started = false;
        }
    }

    private void PollUsnDelta()
    {
        if (!_started)
        {
            return;
        }

        try
        {
            lock (_pollLock)
            {
                var delta = UsnChangeReader.ReadChangesSince(_lastPolledUsn, _watchRoots);
                foreach (var entry in delta)
                {
                    _pendingChanges[entry.RelativePath] = entry;
                }

                _lastPolledUsn = UsnChangeReader.QueryCurrentCheckpoint().NextUsn;
            }
        }
        catch
        {
        }
    }

    public IReadOnlyList<TrackedFileEntry> DrainPendingChanges()
    {
        var drained = _pendingChanges.Values.ToList();
        _pendingChanges.Clear();
        return drained;
    }

    public void StageEntry(TrackedFileEntry entry)
    {
        _pendingChanges[entry.RelativePath] = entry;
    }

    public void Dispose()
    {
        Stop();
        _pendingChanges.Clear();
    }
}
