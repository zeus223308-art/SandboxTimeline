using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace SandboxTimeline;

public sealed class SandboxGuard : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _watchedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recentlyHandled = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _sandboxConfigDir;
    private ProtectedFolderProcessMonitor? _processLaunchMonitor;
    private bool _enabled;
    private bool _paused;

    public SandboxGuard()
    {
        _sandboxConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline",
            "sandbox");
        Directory.CreateDirectory(_sandboxConfigDir);
    }

    public event EventHandler<string>? ProtectedExecutableSandboxed;

    public string? WatchedFolderPath { get; private set; }

    public IReadOnlyList<string> WatchedFolderPaths => _watchedPaths.ToList();

    public bool IsEnabled => _enabled;

    public bool IsPaused => _paused;

    public bool IsWatching => _watchers.Count > 0;

    public static string GetDefaultDownloadsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    public SandboxGuardStartResult TryStart(string? folderPath = null)
    {
        try
        {
            var platformRequirement = SandboxVirtualizationCapability.Evaluate();
            if (!platformRequirement.IsSupported)
            {
                return SandboxGuardStartResult.Blocked(platformRequirement.StatusResourceKey);
            }

            StartWatchingFolders(folderPath);
            return SandboxGuardStartResult.Succeeded();
        }
        catch (Exception ex)
        {
            Stop();
            StartupDiagnostics.Log("SandboxGuard.TryStart failed.", ex);
            return SandboxGuardStartResult.Failed(ex.Message);
        }
    }

    public void Start(string? folderPath = null)
    {
        var result = TryStart(folderPath);
        if (!result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.StatusResourceKey))
            {
                throw new InvalidOperationException(result.StatusResourceKey);
            }

            throw new InvalidOperationException(result.DiagnosticMessage ?? "Sandbox Guard could not start.");
        }
    }

    private void StartWatchingFolders(string? folderPath)
    {
        Stop();

        var pathsToWatch = new List<string>();
        var downloads = GetDefaultDownloadsPath();
        if (Directory.Exists(downloads))
        {
            pathsToWatch.Add(downloads);
        }

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var customFolder = Path.GetFullPath(folderPath);
            if (Directory.Exists(customFolder)
                && !pathsToWatch.Contains(customFolder, StringComparer.OrdinalIgnoreCase))
            {
                pathsToWatch.Add(customFolder);
            }
        }

        if (pathsToWatch.Count == 0)
        {
            throw new DirectoryNotFoundException(
                "No valid watch folder found. Downloads or a custom folder is required.");
        }

        foreach (var fullPath in pathsToWatch)
        {
            AddFolderWatcher(fullPath);
        }

        WatchedFolderPath = pathsToWatch[0];
        _enabled = true;
        _paused = false;

        SharedFolderGuard.EnsureSharedFolderExists();
        _processLaunchMonitor = new ProtectedFolderProcessMonitor(this);
        _processLaunchMonitor.ProtectedExecutableLaunchDetected +=
            (_, executablePath) => ProtectedExecutableSandboxed?.Invoke(this, executablePath);
        _processLaunchMonitor.SetProtectedFolderRoots(_watchedPaths);
        _processLaunchMonitor.StartMonitoring();
    }

    public void PauseWatching()
    {
        if (!_enabled || _paused)
        {
            return;
        }

        _paused = true;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
        }

        _processLaunchMonitor?.StopMonitoring();
    }

    public void ResumeWatching()
    {
        if (!_enabled || !_paused)
        {
            return;
        }

        _paused = false;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = true;
        }

        if (_processLaunchMonitor != null)
        {
            _processLaunchMonitor.SetProtectedFolderRoots(_watchedPaths);
            _processLaunchMonitor.StartMonitoring();
        }
    }

    private void AddFolderWatcher(string fullPath)
    {
        if (_watchedPaths.Contains(fullPath))
        {
            return;
        }

        var watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            Filter = "*.exe",
            EnableRaisingEvents = true
        };

        watcher.Created += OnExecutableDetected;
        watcher.Renamed += OnExecutableRenamed;
        watcher.Changed += OnExecutableDetected;
        _watchers.Add(watcher);
        _watchedPaths.Add(fullPath);
    }

    public void Stop()
    {
        SandboxProcessKillGuard.TerminateAllRegisteredSandboxHostProcesses();

        _processLaunchMonitor?.StopMonitoring();
        _processLaunchMonitor?.Dispose();
        _processLaunchMonitor = null;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnExecutableDetected;
            watcher.Renamed -= OnExecutableRenamed;
            watcher.Changed -= OnExecutableDetected;
            watcher.Dispose();
        }

        _watchers.Clear();
        _watchedPaths.Clear();
        _enabled = false;
        _paused = false;
        WatchedFolderPath = null;
    }

    public bool LaunchInSandbox(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return false;
        }

        if (!IsWindowsSandboxAvailable())
        {
            MessageBox.Show(
                "Windows Sandbox is not enabled on this PC. Enable it via 'Turn Windows features on or off' to isolate untrusted apps.",
                "Sandbox Unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var configPath = BuildSandboxConfig(executablePath);
        var sandboxExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsSandbox.exe");

        Process? sandboxHostProcess = Process.Start(new ProcessStartInfo(sandboxExe, configPath)
        {
            UseShellExecute = true
        });

        if (sandboxHostProcess != null)
        {
            SandboxProcessKillGuard.RegisterSandboxHostProcess(sandboxHostProcess.Id);
        }

        ProtectedExecutableSandboxed?.Invoke(this, executablePath);
        return true;
    }

    private void OnExecutableRenamed(object sender, RenamedEventArgs e)
    {
        if (e.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            HandleExecutable(e.FullPath);
        }
    }

    private void OnExecutableDetected(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            HandleExecutable(e.FullPath);
        }
    }

    private void HandleExecutable(string path)
    {
        if (!_enabled || _paused)
        {
            return;
        }

        lock (_recentlyHandled)
        {
            if (_recentlyHandled.Contains(path))
            {
                return;
            }

            _recentlyHandled.Add(path);
        }

        Task.Run(async () =>
        {
            await WaitForFileReadyAsync(path).ConfigureAwait(false);

            if (System.Windows.Application.Current == null)
            {
                return;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    $"A new executable was detected in a protected folder:\n{path}\n\n" +
                    "Direct execution will be blocked. Run inside Windows Sandbox instead?",
                    "Sandbox Timeline — Untrusted App",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    LaunchInSandbox(path);
                }
            });

            await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            lock (_recentlyHandled)
            {
                _recentlyHandled.Remove(path);
            }
        });
    }

    private static async Task WaitForFileReadyAsync(string path, int maxAttempts = 20)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
    }

    private string BuildSandboxConfig(string executablePath)
    {
        var folder = Path.GetDirectoryName(executablePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fileName = Path.GetFileName(executablePath);
        var mappedFolder = "Downloads";
        var sharedFolderPath = SharedFolderGuard.GetSharedFolderPath();
        SharedFolderGuard.EnsureSharedFolderExists();
        var configPath = Path.Combine(_sandboxConfigDir, $"{Guid.NewGuid():N}.wsb");

        var configBuilder = new StringBuilder();
        configBuilder.AppendLine("<Configuration>");
        configBuilder.AppendLine("  <VGpu>Enable</VGpu>");
        configBuilder.AppendLine("  <Networking>Disable</Networking>");
        configBuilder.AppendLine("  <MappedFolders>");
        configBuilder.AppendLine("    <MappedFolder>");
        configBuilder.AppendLine($"      <HostFolder>{folder}</HostFolder>");
        configBuilder.AppendLine($"      <SandboxFolder>C:\\Users\\WDAGUtilityAccount\\Desktop\\{mappedFolder}</SandboxFolder>");
        configBuilder.AppendLine("      <ReadOnly>false</ReadOnly>");
        configBuilder.AppendLine("    </MappedFolder>");
        configBuilder.AppendLine("    <MappedFolder>");
        configBuilder.AppendLine($"      <HostFolder>{sharedFolderPath}</HostFolder>");
        configBuilder.AppendLine("      <SandboxFolder>C:\\Users\\WDAGUtilityAccount\\Desktop\\Sandbox_Shared</SandboxFolder>");
        configBuilder.AppendLine("      <ReadOnly>false</ReadOnly>");
        configBuilder.AppendLine("    </MappedFolder>");
        configBuilder.AppendLine("  </MappedFolders>");
        configBuilder.AppendLine("  <LogonCommand>");
        configBuilder.AppendLine("    <Command>");
        configBuilder.AppendLine($"      \"C:\\Users\\WDAGUtilityAccount\\Desktop\\{mappedFolder}\\{fileName}\"");
        configBuilder.AppendLine("    </Command>");
        configBuilder.AppendLine("  </LogonCommand>");
        configBuilder.AppendLine("</Configuration>");

        File.WriteAllText(configPath, configBuilder.ToString(), Encoding.UTF8);
        return configPath;
    }

    private static bool IsWindowsSandboxAvailable()
    {
        var sandboxExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsSandbox.exe");
        return File.Exists(sandboxExe);
    }

    public void Dispose()
    {
        Stop();
        _recentlyHandled.Clear();
    }
}
