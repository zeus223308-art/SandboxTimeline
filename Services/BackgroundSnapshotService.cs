using System.Windows.Threading;

namespace SandboxTimeline;

/// <summary>
/// Zero-configuration background snapshot scheduler (hourly by default).
/// </summary>
public sealed class BackgroundSnapshotService : IDisposable
{
    private readonly SnapshotEngine _engine;
    private readonly LicenseManager _license;
    private readonly DispatcherTimer _timer;

    public BackgroundSnapshotService(SnapshotEngine engine, LicenseManager license)
    {
        _engine = engine;
        _license = license;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(1)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        _timer.Start();
        Task.Run(() =>
        {
            try
            {
                _engine.CreateSnapshot("Auto — startup", _license.IsPremium);
                _engine.PruneOldSnapshots(keepCount: 10, _license);
            }
            catch
            {
            }
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                _engine.CreateSnapshot("Auto — scheduled", _license.IsPremium);
                _engine.PruneOldSnapshots(keepCount: 10, _license);
            }
            catch
            {
            }
        });
    }

    private bool _disposed;

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
