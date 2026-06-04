using System.Management;

namespace SandboxTimeline;

/// <summary>
/// Monitors process creation under protected download folders using WMI and Windows Management APIs.
/// Host executables are never terminated directly. Only registered Windows Sandbox host PIDs may be
/// stopped through <see cref="SandboxProcessKillGuard"/>.
/// </summary>
public sealed class ProtectedFolderProcessMonitor : IDisposable
{
    private readonly SandboxGuard _sandboxGuard;
    private readonly HashSet<string> _protectedFolderRoots = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? _processCreationWatcher;
    private bool _isMonitoring;

    public ProtectedFolderProcessMonitor(SandboxGuard sandboxGuard)
    {
        _sandboxGuard = sandboxGuard;
    }

    public event EventHandler<string>? ProtectedExecutableLaunchDetected;

    public void SetProtectedFolderRoots(IEnumerable<string> folderRoots)
    {
        _protectedFolderRoots.Clear();
        foreach (var folderRoot in folderRoots)
        {
            if (string.IsNullOrWhiteSpace(folderRoot))
            {
                continue;
            }

            _protectedFolderRoots.Add(Path.GetFullPath(folderRoot).TrimEnd('\\', '/'));
        }
    }

    public void StartMonitoring()
    {
        if (_isMonitoring || _protectedFolderRoots.Count == 0)
        {
            return;
        }

        try
        {
            var query = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_Process'");

            _processCreationWatcher = new ManagementEventWatcher(query);
            _processCreationWatcher.EventArrived += OnProcessCreated;
            _processCreationWatcher.Start();
            _isMonitoring = true;
        }
        catch
        {
            _isMonitoring = false;
        }
    }

    public void StopMonitoring()
    {
        if (_processCreationWatcher != null)
        {
            _processCreationWatcher.EventArrived -= OnProcessCreated;
            _processCreationWatcher.Stop();
            _processCreationWatcher.Dispose();
            _processCreationWatcher = null;
        }

        _isMonitoring = false;
    }

    private void OnProcessCreated(object sender, EventArrivedEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.NewEvent?["TargetInstance"] is not ManagementBaseObject processInstance)
            {
                return;
            }

            var processId = Convert.ToInt32(processInstance["ProcessId"]);

            if (SandboxProcessKillGuard.IsRegisteredSandboxHostProcess(processId))
            {
                return;
            }

            var executablePath = processInstance["ExecutablePath"]?.ToString();
            if (string.IsNullOrWhiteSpace(executablePath)
                || !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var fullExecutablePath = Path.GetFullPath(executablePath);
            if (!IsUnderProtectedFolderRoot(fullExecutablePath))
            {
                return;
            }

            ProtectedExecutableLaunchDetected?.Invoke(this, fullExecutablePath);

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_sandboxGuard.IsEnabled)
                {
                    _sandboxGuard.LaunchInSandbox(fullExecutablePath);
                }
            });
        }
        catch
        {
        }
    }

    private bool IsUnderProtectedFolderRoot(string executablePath)
    {
        foreach (var folderRoot in _protectedFolderRoots)
        {
            if (executablePath.StartsWith(folderRoot + "\\", StringComparison.OrdinalIgnoreCase)
                || string.Equals(executablePath, folderRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
