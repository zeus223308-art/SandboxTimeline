using System.Text.Json;
using System.Windows.Threading;

namespace SandboxTimeline;

/// <summary>
/// Verifies vault entitlement on startup and once per day to block refund abuse.
/// </summary>
public sealed class LicenseSubscriptionSyncService : IDisposable
{
    private static readonly TimeSpan DailySyncInterval = TimeSpan.FromHours(24);
    private readonly LicenseManager _license;
    private readonly DispatcherTimer _dailyTimer;
    private readonly string _stateFilePath;
    private bool _disposed;

    public LicenseSubscriptionSyncService(LicenseManager license)
    {
        _license = license;
        _stateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline",
            "license-sync-state.json");
        _dailyTimer = new DispatcherTimer
        {
            Interval = DailySyncInterval
        };
        _dailyTimer.Tick += OnDailyTimerTick;
    }

    public void Start()
    {
        _dailyTimer.Start();
    }

    private void OnDailyTimerTick(object? sender, EventArgs e)
    {
        _ = RunSyncAsync(forceImmediate: false);
    }

    private bool ShouldRunDailySync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return true;
            }

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<LicenseSubscriptionSyncState>(json);
            if (state?.LastSuccessfulSyncUtc == null)
            {
                return true;
            }

            return DateTime.UtcNow - state.LastSuccessfulSyncUtc.Value >= DailySyncInterval;
        }
        catch
        {
            return true;
        }
    }

    private async Task RunSyncAsync(bool forceImmediate)
    {
        if (!forceImmediate && !ShouldRunDailySync())
        {
            return;
        }

        try
        {
            await _license.SyncSubscriptionEntitlementAsync().ConfigureAwait(false);
            WriteSyncState(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("License subscription sync failed.", ex);
        }
    }

    private void WriteSyncState(DateTime syncedAtUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            var state = new LicenseSubscriptionSyncState { LastSuccessfulSyncUtc = syncedAtUtc };
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state));
        }
        catch
        {
        }
    }

    public void Stop()
    {
        _dailyTimer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dailyTimer.Stop();
        _dailyTimer.Tick -= OnDailyTimerTick;
    }

    private sealed class LicenseSubscriptionSyncState
    {
        public DateTime? LastSuccessfulSyncUtc { get; set; }
    }
}
