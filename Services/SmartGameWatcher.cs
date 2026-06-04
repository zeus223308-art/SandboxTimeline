using System.Diagnostics;

namespace SandboxTimeline;

/// <summary>
/// Pauses global hotkeys and sandbox monitoring while major online games are running
/// to reduce conflicts with game anti-cheat services.
/// </summary>
public sealed class SmartGameWatcher : IDisposable
{
    private static readonly string[] WatchedProcessNames =
    [
        "League of Legends",
        "LeagueClient",
        "LeagueClientUx",
        "VALORANT",
        "Valorant",
        "VALORANT-Win64-Shipping",
        "TslGame",
        "cs2",
        "FortniteClient-Win64-Shipping",
        "Overwatch",
        "cod",
        "r5apex"
    ];

    private readonly object _sync = new();
    private System.Threading.Timer? _pollTimer;
    private GlobalHotkeyService? _hotkey;
    private SandboxGuard? _sandbox;
    private bool _gameModeActive;
    private bool _disposed;

    public bool IsGameModeActive
    {
        get
        {
            lock (_sync)
            {
                return _gameModeActive;
            }
        }
    }

    public event EventHandler<bool>? GameModeChanged;

    public void Start(GlobalHotkeyService hotkey, SandboxGuard sandbox)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            _hotkey = hotkey;
            _sandbox = sandbox;
            _pollTimer?.Dispose();
            _pollTimer = new System.Threading.Timer(PollProcesses, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    private void PollProcesses(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var gameRunning = IsAnyWatchedGameRunning();

        lock (_sync)
        {
            if (gameRunning && !_gameModeActive)
            {
                EnterGameMode();
            }
            else if (!gameRunning && _gameModeActive)
            {
                ExitGameMode();
            }
        }
    }

    private static bool IsAnyWatchedGameRunning()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var name = process.ProcessName;
                    if (WatchedProcessNames.Any(
                            watched => name.Contains(watched, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private void EnterGameMode()
    {
        _gameModeActive = true;
        _hotkey?.Pause();
        _sandbox?.PauseWatching();
        GameModeChanged?.Invoke(this, true);
    }

    private void ExitGameMode()
    {
        _gameModeActive = false;
        _hotkey?.Resume();
        _sandbox?.ResumeWatching();
        GameModeChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;

            if (_gameModeActive)
            {
                ExitGameMode();
            }

            _hotkey = null;
            _sandbox = null;
        }
    }
}
