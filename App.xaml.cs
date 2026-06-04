using System.Windows;
using System.Windows.Threading;

namespace SandboxTimeline;

public partial class App : System.Windows.Application
{
    private BackgroundSnapshotService? _backgroundService;
    private LicenseSubscriptionSyncService? _licenseSubscriptionSync;
    private SmartGameWatcher? _smartGameWatcher;
    private bool _cleanupPerformed;

    public SnapshotEngine? Engine { get; private set; }
    public SandboxGuard? Sandbox { get; private set; }
    public LicenseManager? License { get; private set; }
    public BackgroundSnapshotService? BackgroundService => _backgroundService;
    public TrayIconService? Tray { get; private set; }
    public GlobalHotkeyService? Hotkey { get; private set; }
    public bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupDiagnostics.Log("App.OnStartup begin.");

        LocalizationService.ApplyStartupCulture();
        LogWindowsEditionCapability();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        SessionEnding += OnSessionEnding;

        MainWindow? mainWindow = null;

        try
        {
            StartupDiagnostics.Log("Creating MainWindow shell.");
            mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.ShowStartupShell();
            StartupDiagnostics.Log("MainWindow shown.");

            if (await TryApplySilentAutoUpdateAsync(mainWindow, e.Args))
            {
                StartupDiagnostics.Log("Silent auto-update relaunch sequence initiated.");
                IsShuttingDown = true;
                Shutdown(0);
                return;
            }

            await AppContinueStartupAsync(mainWindow);
            StartupDiagnostics.Log("App.OnStartup completed successfully.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("App.OnStartup failed.", ex);

            if (mainWindow != null)
            {
                ForcePresentMainWindow(mainWindow);
                mainWindow.ReportFatalStartupFailure(ex);
            }
            else
            {
                MessageBox.Show(
                    ExceptionDisplayFormatter.FormatWithPrefix("Sandbox Timeline failed to start:", ex) +
                    Environment.NewLine + Environment.NewLine +
                    $"Log file: {StartupDiagnostics.LogPath}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                PerformApplicationCleanup();
                Shutdown(1);
            }
        }
    }

    private static async Task<bool> TryApplySilentAutoUpdateAsync(MainWindow mainWindow, string[] startupArguments)
    {
        try
        {
            var updateManager = new UpdateManager(mainWindow, startupArguments);
            return await updateManager.TrySilentUpdateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Silent auto-update gate failed open (bypass).", ex);
            return false;
        }
    }

    private async Task AppContinueStartupAsync(MainWindow mainWindow)
    {
        try
        {
            SingleInstanceManager.StartActivationListener(ActivateMainWindow);

            StartupDiagnostics.Log("Creating LicenseManager.");
            License = new LicenseManager();

            StartupDiagnostics.Log("Creating SnapshotEngine.");
            Engine = new SnapshotEngine();

            StartupDiagnostics.Log("Creating SandboxGuard.");
            Sandbox = new SandboxGuard();

            StartupDiagnostics.Log("Attaching services to MainWindow.");
            mainWindow.AttachApplicationServices(Engine, Sandbox, License);

            try
            {
                _licenseSubscriptionSync = new LicenseSubscriptionSyncService(License);
                _licenseSubscriptionSync.Start();
                StartupDiagnostics.Log("License subscription sync service started.");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log("License subscription sync failed; continuing startup.", ex);
            }

            if (!string.IsNullOrWhiteSpace(License.LastStartupDiagnostic))
            {
                try
                {
                    mainWindow.ReportStartupWarning(License.LastStartupDiagnostic);
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.Log("Startup warning presentation failed.", ex);
                }
            }

            TryInitializeTrayIconSafely(mainWindow);
            TryInitializeGlobalHotkeySafely(mainWindow);
            TryInitializeSmartGameWatcherSafely(mainWindow);
            TryInitializeBackgroundSnapshotServiceSafely();

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("AppContinueStartupAsync failed; recovering main window.", ex);
            try
            {
                mainWindow.ReportPartialStartupFailure(
                    "Some startup services failed. The main window will stay open.",
                    ex);
            }
            catch (Exception reportEx)
            {
                StartupDiagnostics.Log("Partial startup failure report failed.", reportEx);
            }
        }
        finally
        {
            ForcePresentMainWindow(mainWindow);
        }
    }

    private void TryInitializeTrayIconSafely(MainWindow mainWindow)
    {
        try
        {
            Tray = new TrayIconService(mainWindow);
            StartupDiagnostics.Log("Tray icon created.");
        }
        catch (Exception ex)
        {
            Tray = null;
            StartupDiagnostics.Log("Tray icon creation failed; bypassing tray.", ex);
        }
    }

    private void TryInitializeGlobalHotkeySafely(MainWindow mainWindow)
    {
        try
        {
            Hotkey = new GlobalHotkeyService();
            Hotkey.HotkeyPressed += OnGlobalHotkeyPressed;
            Hotkey.Register();
            StartupDiagnostics.Log("Global hotkey registered.");
        }
        catch (Exception ex)
        {
            Hotkey = null;
            StartupDiagnostics.Log("Global hotkey registration failed.", ex);
            try
            {
                mainWindow.ReportPartialStartupFailure(
                    "Global hotkey (Ctrl + Win + Z) could not be registered. Use the taskbar window to reopen the app.",
                    ex);
            }
            catch (Exception reportEx)
            {
                StartupDiagnostics.Log("Hotkey failure report failed.", reportEx);
            }
        }
    }

    private void TryInitializeSmartGameWatcherSafely(MainWindow mainWindow)
    {
        try
        {
            if (Hotkey == null || Sandbox == null)
            {
                return;
            }

            _smartGameWatcher = new SmartGameWatcher();
            _smartGameWatcher.GameModeChanged += (_, active) =>
                mainWindow.ReportGameModeChanged(active);
            _smartGameWatcher.Start(Hotkey, Sandbox);
            StartupDiagnostics.Log("Smart game watcher started.");
        }
        catch (Exception ex)
        {
            _smartGameWatcher = null;
            StartupDiagnostics.Log("Smart game watcher failed; continuing startup.", ex);
        }
    }

    private void TryInitializeBackgroundSnapshotServiceSafely()
    {
        try
        {
            if (Engine == null || License == null)
            {
                return;
            }

            _backgroundService = new BackgroundSnapshotService(Engine, License);
            _backgroundService.Start();
            StartupDiagnostics.Log("Background snapshot service started.");
        }
        catch (Exception ex)
        {
            _backgroundService = null;
            StartupDiagnostics.Log("Background snapshot service failed; continuing startup.", ex);
        }
    }

    private static void ForcePresentMainWindow(MainWindow mainWindow)
    {
        try
        {
            if (mainWindow.Dispatcher.CheckAccess())
            {
                mainWindow.ForceStartupPresentation();
            }
            else
            {
                mainWindow.Dispatcher.Invoke(mainWindow.ForceStartupPresentation);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("ForcePresentMainWindow failed; attempting Show().", ex);
            try
            {
                if (mainWindow.Dispatcher.CheckAccess())
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                }
                else
                {
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        mainWindow.Show();
                        mainWindow.Activate();
                    });
                }
            }
            catch (Exception showEx)
            {
                StartupDiagnostics.Log("Emergency main window Show() failed.", showEx);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupDiagnostics.Log("App.OnExit.");
        PerformApplicationCleanup();
        base.OnExit(e);
    }

    private static void LogWindowsEditionCapability()
    {
        try
        {
            var edition = WindowsEditionCapability.Evaluate();
            StartupDiagnostics.Log(
                $"Windows edition: {edition.ProductName} ({edition.EditionId}), " +
                $"Windows 11={edition.IsWindows11}, Home={edition.IsHomeEdition}, " +
                $"SandboxGuardSupported={edition.SupportsSandboxGuard}.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Windows edition detection failed.", ex);
        }
    }

    public void ActivateMainWindow()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowAndActivateTimeline();
            }
        });
    }

    public void ShutdownFromTray()
    {
        IsShuttingDown = true;
        Shutdown();
    }

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e)
    {
        ActivateMainWindow();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        IsShuttingDown = true;
        PerformApplicationCleanup();
    }

    private void PerformApplicationCleanup()
    {
        if (_cleanupPerformed)
        {
            return;
        }

        _cleanupPerformed = true;

        Hotkey?.Dispose();
        Hotkey = null;

        _smartGameWatcher?.Dispose();
        _smartGameWatcher = null;

        Tray?.Dispose();
        Tray = null;

        _backgroundService?.Stop();
        _backgroundService?.Dispose();
        _backgroundService = null;

        Sandbox?.Stop();
        Sandbox?.Dispose();
        Sandbox = null;

        Engine?.Shutdown();
        Engine?.Dispose();
        Engine = null;

        License?.Dispose();
        License = null;

        _licenseSubscriptionSync?.Dispose();
        _licenseSubscriptionSync = null;

        SingleInstanceManager.Release();

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        SessionEnding -= OnSessionEnding;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Log("Dispatcher unhandled exception.", e.Exception);

        if (MainWindow is MainWindow mainWindow)
        {
            ForcePresentMainWindow(mainWindow);

            if (IsTrayRelatedException(e.Exception))
            {
                StartupDiagnostics.Log("Tray-related dispatcher exception suppressed.", e.Exception);
                e.Handled = true;
                return;
            }

            mainWindow.ReportFatalStartupFailure(e.Exception);
            e.Handled = true;
            return;
        }

        MessageBox.Show(
            ExceptionDisplayFormatter.FormatWithPrefix("An unexpected error occurred:", e.Exception),
            "Sandbox Timeline",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static bool IsTrayRelatedException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var source = current.Source ?? string.Empty;
            var stack = current.StackTrace ?? string.Empty;
            if (current is InvalidOperationException or NullReferenceException)
            {
                if (stack.Contains("TrayIconService", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("TrayIconService", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            StartupDiagnostics.Log("AppDomain unhandled exception.", ex);
            MessageBox.Show(
                ExceptionDisplayFormatter.FormatWithPrefix("A fatal error occurred:", ex) +
                Environment.NewLine + Environment.NewLine +
                $"Log file: {StartupDiagnostics.LogPath}",
                "Sandbox Timeline",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
