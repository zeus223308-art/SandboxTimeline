using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ModernWpf.Controls;

namespace SandboxTimeline;

public partial class MainWindow : Window
{
    private const uint WmNull = 0x0000;

    private SnapshotEngine _engine = null!;
    private SandboxGuard _sandbox = null!;
    private LicenseManager _license = null!;
    private BackgroundSnapshotService? _background;
    private IReadOnlyList<SnapshotInfo> _snapshots = Array.Empty<SnapshotInfo>();
    private bool _isRollingBack;
    private bool _isLicenseFailureVisible;
    private bool _servicesAttached;
    private bool _mainExperienceStarted;
    private bool _lastKnownPremium;
    private SandboxVirtualizationCapability? _sandboxVirtualizationCapability;
    private ContextMenu? _trayContextMenu;

    public MainWindow()
    {
        InitializeComponent();
        ApplyHotkeyDisplayStrings();
        WindowBackdropHelper.TryApplyMica(this);
        SetStartupShellState();
        InitializeTrayContextMenu();
    }

    /// <summary>
    /// DI constructor for tests or manual startup.
    /// </summary>
    public MainWindow(
        SnapshotEngine engine,
        SandboxGuard sandbox,
        LicenseManager license,
        BackgroundSnapshotService? background)
    {
        InitializeComponent();
        ApplyHotkeyDisplayStrings();
        WindowBackdropHelper.TryApplyMica(this);
        _engine = engine;
        _sandbox = sandbox;
        _license = license;
        _background = background;
        _servicesAttached = true;
        WireEvents();
        EnableTimelineControls(true);
        InitializeTrayContextMenu();
    }

    public void ShowStartupShell()
    {
        Show();
        Activate();
        Focus();
        WindowState = WindowState.Normal;
        Topmost = true;
        Topmost = false;
    }

    public void ForceStartupPresentation()
    {
        ShowStartupShell();

        try
        {
            if (_servicesAttached)
            {
                EnableTimelineControls(true);
                EnsureMainExperienceStarted();
                TryShowWelcomeGuide();
                EnsureTimelineInteractive();
                return;
            }

            if (string.IsNullOrWhiteSpace(StatusText.Text))
            {
                StatusText.Text = Loc.Get("Str_StartupLoading");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("ForceStartupPresentation failed.", ex);
        }
    }

    public void ReportAutoUpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    public void AttachApplicationServices(
        SnapshotEngine engine,
        SandboxGuard sandbox,
        LicenseManager license)
    {
        _engine = engine;
        _sandbox = sandbox;
        _license = license;
        _background = (System.Windows.Application.Current as App)?.BackgroundService;
        _servicesAttached = true;
        _lastKnownPremium = _license.IsPremium;
        WireEvents();
        ConfigureLicensePurchaseUi();
        EnableTimelineControls(true);
        RefreshSandboxVirtualizationCapability();
        EnsureMainExperienceStarted();
    }

    private void RefreshSandboxVirtualizationCapability()
    {
        _sandboxVirtualizationCapability = SandboxVirtualizationCapability.Evaluate();
    }

    private void ApplySandboxToggleEnabledState()
    {
        if (!_servicesAttached)
        {
            SandboxToggleButton.IsEnabled = false;
            return;
        }

        if (_sandbox.IsWatching)
        {
            SandboxToggleButton.IsEnabled = !_isRollingBack;
            return;
        }

        _sandboxVirtualizationCapability ??= SandboxVirtualizationCapability.Evaluate();
        if (!_license.CanUseAutoSandbox())
        {
            SandboxToggleButton.IsEnabled = !_isRollingBack;
            return;
        }

        SandboxToggleButton.IsEnabled =
            _sandboxVirtualizationCapability.IsSupported &&
            !_isRollingBack;
    }

    private void ShowSandboxCapabilityBlockedStatus()
    {
        _sandboxVirtualizationCapability ??= SandboxVirtualizationCapability.Evaluate();
        if (_sandboxVirtualizationCapability.IsSupported || _isLicenseFailureVisible)
        {
            return;
        }

        StatusText.Text = Loc.Get(_sandboxVirtualizationCapability.StatusResourceKey);
    }

    public void ReportFatalStartupFailure(Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            ShowStartupShell();
            EnableTimelineControls(false);
            StatusText.Text = Loc.Get("Str_StartupFailedStatus");
            MessageBox.Show(
                ExceptionDisplayFormatter.FormatWithPrefix("Sandbox Timeline failed to start:", ex) +
                Environment.NewLine + Environment.NewLine +
                $"Log file: {StartupDiagnostics.LogPath}",
                Loc.Get("Str_WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    private void SetStartupShellState()
    {
        StatusText.Text = Loc.Get("Str_StartupLoading");
        EnableTimelineControls(false);
    }

    private void EnableTimelineControls(bool enabled)
    {
        TakeSnapshotButton.IsEnabled = enabled && !_isRollingBack;
        RollbackButton.IsEnabled = enabled && _snapshots.Count > 0;
        TimelineSlider.IsEnabled = enabled && _snapshots.Count > 0;
        ApplySandboxToggleEnabledState();
        ValidateLicenseButton.IsEnabled = enabled;
        LicenseKeyBox.IsEnabled = enabled;
    }

    private void BindApplicationServices()
    {
        if (System.Windows.Application.Current is not App app)
        {
            throw new InvalidOperationException("Application host must be SandboxTimeline.App.");
        }

        AttachApplicationServices(
            app.Engine ?? throw new InvalidOperationException("SnapshotEngine is not initialized."),
            app.Sandbox ?? throw new InvalidOperationException("SandboxGuard is not initialized."),
            app.License ?? throw new InvalidOperationException("LicenseManager is not initialized."));
    }

    private void WireEvents()
    {
        if (!_servicesAttached)
        {
            return;
        }

        _engine.StatusChanged -= OnEngineStatusChanged;
        _sandbox.ProtectedExecutableSandboxed -= OnProtectedExecutableSandboxed;
        _license.LicenseChanged -= OnLicenseChanged;

        _engine.StatusChanged += OnEngineStatusChanged;
        _sandbox.ProtectedExecutableSandboxed += OnProtectedExecutableSandboxed;
        _license.LicenseChanged += OnLicenseChanged;

        Loaded -= OnLoaded;
        Loaded += OnLoaded;
        Closing -= OnClosing;
        Closing += OnClosing;
    }

    private void OnEngineStatusChanged(object? sender, string msg)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isLicenseFailureVisible)
            {
                StatusText.Text = msg;
            }
        });
    }

    private void OnProtectedExecutableSandboxed(object? sender, string path)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrEmpty(_sandbox.WatchedFolderPath))
            {
                StatusText.Text = Loc.Format(
                    "Str_SandboxedWithPathFormat",
                    FormatSandboxGuardingStatus(_sandbox.WatchedFolderPath),
                    path);
            }
            else
            {
                StatusText.Text = Loc.Format("Str_SandboxedExecutableFormat", path);
            }
        });
    }

    private void OnLicenseChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var wasPremium = _lastKnownPremium;
            _lastKnownPremium = _license.IsPremium;
            RefreshLicenseUi();
            UpdateSandboxGuardUi();
            ShowSandboxCapabilityBlockedStatus();

            if (wasPremium && !_license.IsPremium)
            {
                if (_sandbox.IsWatching)
                {
                    _sandbox.Stop();
                    SandboxGuardPreferences.Save(null, false);
                }

                LicenseExpander.Visibility = Visibility.Visible;
                LicenseExpander.IsExpanded = true;
                StatusText.Text = !string.IsNullOrWhiteSpace(_license.LastStartupDiagnostic)
                    ? _license.LastStartupDiagnostic
                    : Loc.Get("Str_LicenseSubscriptionRevoked");
            }
        });
    }

    private void ApplyHotkeyDisplayStrings()
    {
        try
        {
            var hotkey = Loc.Get("Str_GlobalHotkeyDisplay");
            AppSubtitleText.Text = Loc.Format("Str_SubtitleFormat", hotkey);
            WelcomeHotkeyBodyText.Text = Loc.Format("Str_WelcomeHotkeyBodyFormat", hotkey);
            ApplyPremiumMarketingCopy();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("ApplyHotkeyDisplayStrings failed.", ex);
            AppSubtitleText.Text = "Sandbox Timeline";
            WelcomeHotkeyBodyText.Text = GlobalHotkeyConfiguration.DisplayName;
        }
    }

    private void ApplyPremiumMarketingCopy()
    {
        PremiumTaglineText.Text = Loc.Get("Str_PremiumRecoveryTagline");
    }

    public void ReportGameModeChanged(bool active)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isLicenseFailureVisible)
            {
                return;
            }

            StatusText.Text = active
                ? Loc.Get("Str_GameModePausedStatus")
                : Loc.Get("Str_StatusReady");
        });
    }

    private void OnSnapshotCreated(SnapshotInfo snap)
    {
        ReloadSnapshots();
        TimelineSlider.Value = TimelineSlider.Maximum;
        UpdateSelectedSnapshotUi(SliderValueToSnapshotIndex(TimelineSlider.Value));
        StatusText.Text = Loc.Format("Str_SnapshotReadyFormat", snap.SnapshotNumber, snap.ChangedFileCount);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsShuttingDown: false })
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ShowAndActivateTimeline()
    {
        Show();
        WindowState = WindowState.Normal;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + (workArea.Height - ActualHeight) / 2;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        ReloadSnapshots();
        TimelineSlider.IsEnabled = _snapshots.Count > 0;
        TimelineSlider.Focus();
        Keyboard.Focus(TimelineSlider);

        StatusText.Text = _snapshots.Count > 0
            ? Loc.Get("Str_TimeSliderReady")
            : Loc.Get("Str_NoSnapshotsTakeFirst");
    }

    /// <summary>
    /// Shows the tray context menu anchored to the current mouse cursor.
    /// SetForegroundWindow must run on the tray message window before opening the menu,
    /// otherwise Windows misplaces the popup and outside clicks fail to dismiss it.
    /// </summary>
    public void ShowTrayContextMenuAtCursor(IntPtr trayMessageWindowHandle)
    {
        if (trayMessageWindowHandle == IntPtr.Zero || _trayContextMenu == null)
        {
            return;
        }

        ShellTrayIcon.GetCursorPos(out _);

        SetForegroundWindow(trayMessageWindowHandle);

        _trayContextMenu.PlacementTarget = this;
        _trayContextMenu.Placement = PlacementMode.MousePoint;
        _trayContextMenu.HorizontalOffset = 0;
        _trayContextMenu.VerticalOffset = 0;
        _trayContextMenu.IsOpen = false;
        _trayContextMenu.IsOpen = true;

        PostMessage(trayMessageWindowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
    }

    private void InitializeTrayContextMenu()
    {
        _trayContextMenu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        var openMenuItem = new MenuItem { Header = "열기 (Open)" };
        openMenuItem.Click += TrayContextMenuOpen_Click;

        var exitMenuItem = new MenuItem { Header = "종료 (Exit)" };
        exitMenuItem.Click += TrayContextMenuExit_Click;

        _trayContextMenu.Items.Add(openMenuItem);
        _trayContextMenu.Items.Add(new Separator());
        _trayContextMenu.Items.Add(exitMenuItem);
    }

    private void TrayContextMenuOpen_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivateTimeline();
    }

    private void TrayContextMenuExit_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ShutdownFromTray();
        }
        else
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void ReportStartupWarning(Exception exception)
    {
        ReportStartupWarning();
    }

    public void ReportStartupWarning(string diagnostic)
    {
        ReportStartupWarning();
    }

    public void ReportStartupWarning()
    {
        Dispatcher.Invoke(() =>
        {
            if (!_license.IsPremium)
            {
                ShowLicenseFailureUi();
            }

            EnsureTimelineInteractive();
        });
    }

    public void ReportPartialStartupFailure(string message, Exception ex)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ReportPartialStartupFailure(message, ex));
            return;
        }

        try
        {
            ShowStartupShell();
            StatusText.Text = message;
            MessageBox.Show(
                ExceptionDisplayFormatter.FormatWithPrefix(message, ex),
                Loc.Get("Str_WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception reportEx)
        {
            StartupDiagnostics.Log("ReportPartialStartupFailure failed.", reportEx);
        }

        try
        {
            if (_servicesAttached)
            {
                EnsureTimelineInteractive();
            }
        }
        catch (Exception interactiveEx)
        {
            StartupDiagnostics.Log("EnsureTimelineInteractive after partial failure failed.", interactiveEx);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_servicesAttached)
        {
            return;
        }

        _engine.SnapshotCreated += (_, snap) => Dispatcher.Invoke(() => OnSnapshotCreated(snap));

        try
        {
            await EnsureMainExperienceStartedAsync();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("InitializeMainExperienceAsync failed.", ex);
            ReportFatalStartupFailure(ex);
        }
    }

    private void EnsureMainExperienceStarted()
    {
        if (!_servicesAttached || _mainExperienceStarted)
        {
            return;
        }

        _mainExperienceStarted = true;
        _ = InitializeMainExperienceAsync();
    }

    private Task EnsureMainExperienceStartedAsync()
    {
        if (!_servicesAttached)
        {
            return Task.CompletedTask;
        }

        if (_mainExperienceStarted)
        {
            return Task.CompletedTask;
        }

        _mainExperienceStarted = true;
        return InitializeMainExperienceAsync();
    }

    private async Task InitializeMainExperienceAsync()
    {
        try
        {
            await _license.SyncSubscriptionEntitlementAsync().ConfigureAwait(true);
        }
        catch
        {
            if (!_license.IsPremium)
            {
                ShowLicenseFailureUi();
            }
        }

        RefreshLicenseUi();
        _lastKnownPremium = _license.IsPremium;

#if DEBUG
        if (DeveloperLicenseResetPolicy.ForceClearStoredLicenseOnStartup && !_license.IsPremium)
        {
            LicenseExpander.Visibility = Visibility.Visible;
            LicenseExpander.IsExpanded = true;
            ClearLicenseFailureUi();
            ResetStatusTextStyle();
            StatusText.Text = Loc.Get("Str_StatusReady");
        }
        else
#endif
        if (_license.IsPremium)
        {
            ApplyPremiumSuccessStatus(Loc.Get("Str_PremiumActivatedThanks"));
        }
        else if (!string.IsNullOrWhiteSpace(_license.LastStartupDiagnostic))
        {
            ShowLicenseFailureUi();
            StatusText.Text = Loc.Get("Str_LicenseSubscriptionRevoked");
        }

        try
        {
            ReloadSnapshots();
        }
        catch (Exception ex)
        {
            StatusText.Text = ExceptionDisplayFormatter.FormatWithPrefix("Snapshot list load failed:", ex);
        }

        try
        {
            TryRestoreSandboxGuard();
        }
        catch (Exception ex)
        {
            StatusText.Text = ExceptionDisplayFormatter.FormatWithPrefix("Sandbox guard restore failed:", ex);
        }

        try
        {
            TryShowWelcomeGuide();
        }
        catch
        {
        }

        EnsureTimelineInteractive();
    }

    private void EnsureTimelineInteractive()
    {
        TakeSnapshotButton.IsEnabled = !_isRollingBack;
        RollbackButton.IsEnabled = _snapshots.Count > 0;
        TimelineSlider.IsEnabled = _snapshots.Count > 0;
        UpdateSandboxGuardUi();

        if (string.IsNullOrWhiteSpace(StatusText.Text))
        {
            StatusText.Text = _snapshots.Count > 0
                ? Loc.Get("Str_TimeSliderReady")
                : Loc.Get("Str_NoSnapshotsTakeFirst");
        }

        ShowSandboxCapabilityBlockedStatus();
    }

    private void TryRestoreSandboxGuard()
    {
        if (!_license.CanUseAutoSandbox())
        {
            UpdateSandboxGuardUi();
            return;
        }

        RefreshSandboxVirtualizationCapability();
        if (!_sandboxVirtualizationCapability!.IsSupported)
        {
            UpdateSandboxGuardUi();
            ShowSandboxCapabilityBlockedStatus();
            return;
        }

        var prefs = SandboxGuardPreferences.Load();
        if (!prefs.GuardEnabled ||
            string.IsNullOrWhiteSpace(prefs.WatchFolderPath) ||
            !Directory.Exists(prefs.WatchFolderPath))
        {
            UpdateSandboxGuardUi();
            return;
        }

        try
        {
            var startResult = _sandbox.TryStart(prefs.WatchFolderPath);
            if (startResult.Success)
            {
                StatusText.Text = FormatSandboxGuardingStatus(prefs.WatchFolderPath);
            }
            else
            {
                SandboxGuardPreferences.Save(null, false);
                if (!string.IsNullOrWhiteSpace(startResult.StatusResourceKey))
                {
                    StatusText.Text = Loc.Get(startResult.StatusResourceKey);
                }
            }
        }
        catch
        {
            SandboxGuardPreferences.Save(null, false);
        }

        UpdateSandboxGuardUi();
    }

    private void UpdateSandboxGuardUi()
    {
        SandboxToggleButton.Content = _sandbox.IsWatching
            ? Loc.Get("Str_SandboxGuardOn")
            : Loc.Get("Str_SandboxGuardOff");
        ApplySandboxToggleEnabledState();
    }

    private static string FormatSandboxGuardingStatus(string? folderPath)
    {
        var downloads = SandboxGuard.GetDefaultDownloadsPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Loc.Format("Str_SandboxGuardingFormat", downloads);
        }

        if (folderPath.Equals(downloads, StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Format("Str_SandboxGuardingFormat", downloads);
        }

        return Loc.Format("Str_SandboxGuardingTwoFormat", downloads, folderPath);
    }

    private void TryShowWelcomeGuide()
    {
        if (!UserGuidePreferences.ShouldShowWelcomeGuide())
        {
            return;
        }

        var availableHeight = Math.Max(380, ActualHeight - 56);
        WelcomeDialogCard.MaxHeight = Math.Min(540, availableHeight);
        WelcomeDialogCard.MinHeight = Math.Min(420, availableHeight);

        WelcomeOverlay.Visibility = Visibility.Visible;
        WelcomeOverlay.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        WelcomeOverlay.BeginAnimation(OpacityProperty, fadeIn);

        var scaleIn = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
        };
        WelcomeOverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        WelcomeOverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
    }

    private void WelcomeGuideDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (DontShowWelcomeAgainCheckBox.IsChecked == true)
        {
            UserGuidePreferences.SetSkipWelcomeGuide(true);
        }

        CloseWelcomeGuide();
    }

    private void CloseWelcomeGuide()
    {
        WelcomeOverlay.BeginAnimation(OpacityProperty, null);
        WelcomeDialogCard.BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation(WelcomeOverlay.Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            WelcomeOverlay.Opacity = 1;
            WelcomeOverlayScale.ScaleX = 1;
            WelcomeOverlayScale.ScaleY = 1;
            FocusMainTimeline();
        };
        WelcomeOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void FocusMainTimeline()
    {
        TimelineSlider.IsEnabled = _snapshots.Count > 0 || TakeSnapshotButton.IsEnabled;
        TimelineSlider.Focus();
        Keyboard.Focus(TimelineSlider);
        StatusText.Text = _snapshots.Count > 0
            ? Loc.Get("Str_TimeSliderReady")
            : Loc.Get("Str_NoSnapshotsTakeFirst");
    }

    private void ReloadSnapshots()
    {
        _snapshots = _engine.ListSnapshots();
        SnapshotCountLabel.Text = Loc.Format("Str_SnapshotCountFormat", _snapshots.Count);

        if (_snapshots.Count == 0)
        {
            TimelineSlider.Minimum = 0;
            TimelineSlider.Maximum = 0;
            TimelineSlider.Value = 0;
            TimelineSlider.IsEnabled = false;
            SelectedSnapshotLabel.Text = Loc.Get("Str_NoSnapshotsYet");
            SelectedSnapshotTime.Text = Loc.Get("Str_FirstSnapshotHint");
            return;
        }

        TimelineSlider.IsEnabled = true;
        TimelineSlider.Minimum = 0;
        TimelineSlider.Maximum = _snapshots.Count - 1;
        TimelineSlider.TickFrequency = 1;
        TimelineSlider.IsSnapToTickEnabled = true;
        TimelineSlider.Value = TimelineSlider.Maximum;
        UpdateSelectedSnapshotUi(SliderValueToSnapshotIndex(TimelineSlider.Value));
    }

    private int SliderValueToSnapshotIndex(double sliderValue)
    {
        if (_snapshots.Count == 0)
        {
            return 0;
        }

        var sliderIndex = (int)Math.Round(sliderValue);
        sliderIndex = Math.Clamp(sliderIndex, 0, _snapshots.Count - 1);
        return _snapshots.Count - 1 - sliderIndex;
    }

    private void UpdateSelectedSnapshotUi(int index)
    {
        if (index < 0 || index >= _snapshots.Count)
        {
            return;
        }

        var snap = _snapshots[index];
        SelectedSnapshotLabel.Text = snap.DisplayLabel;
        var mode = snap.UsedUsnJournal ? Loc.Get("Str_TrackingUsn") : Loc.Get("Str_TrackingHash");
        SelectedSnapshotTime.Text = Loc.Format(
            "Str_SnapshotDetailFormat",
            snap.CreatedAtUtc.ToLocalTime().ToString("F"),
            snap.ChangedFileCount,
            mode);
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_snapshots.Count == 0)
        {
            return;
        }

        var index = SliderValueToSnapshotIndex(TimelineSlider.Value);
        UpdateSelectedSnapshotUi(index);
    }

    private async void TakeSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TakeSnapshotButtonState(false);
            StatusText.Text = Loc.Get("Str_CreatingSnapshot");
            await Task.Run(() => _engine.CreateSnapshot(isPremium: _license.IsPremium));
            _engine.PruneOldSnapshots(10, _license);
            ReloadSnapshots();
            StatusText.Text = Loc.Get("Str_SnapshotSaved");
        }
        catch (UnauthorizedAccessException ex)
        {
            var details = ExceptionDisplayFormatter.Format(ex);
            StatusText.Text = Loc.Format("Str_ErrorPrefix", details);
            await ShowErrorAsync(Loc.Get("Str_AdminRequired"), details);
        }
        catch (IOException ex)
        {
            var details = ExceptionDisplayFormatter.Format(ex);
            StatusText.Text = Loc.Format("Str_ErrorPrefix", details);
            await ShowErrorAsync(Loc.Get("Str_DiskIoError"), details);
        }
        catch (Exception ex)
        {
            var details = ExceptionDisplayFormatter.Format(ex);
            StatusText.Text = Loc.Format("Str_SnapshotFailedFormat", details);
            await ShowErrorAsync(Loc.Get("Str_SnapshotError"), details);
        }
        finally
        {
            TakeSnapshotButtonState(true);
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshots.Count == 0)
        {
            await ShowInfoAsync(Loc.Get("Str_Rollback"), Loc.Get("Str_RollbackNeedSnapshot"));
            return;
        }

        var index = SliderValueToSnapshotIndex(TimelineSlider.Value);
        var snap = _snapshots[index];

        var confirm = MessageBox.Show(
            Loc.Format(
                "Str_ConfirmRollbackBody",
                snap.Label,
                snap.CreatedAtUtc.ToLocalTime().ToString("F")),
            Loc.Get("Str_ConfirmRollbackTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _isRollingBack = true;
            RollbackButtonState(false);
            StatusText.Text = Loc.Get("Str_RollingBack");
            await Task.Run(() => _engine.RollbackToSnapshot(snap.Id));
            StatusText.Text = Loc.Get("Str_RollbackFinished");
        }
        catch (Exception ex)
        {
            var details = ExceptionDisplayFormatter.Format(ex);
            StatusText.Text = Loc.Format("Str_RollbackFailedFormat", details);
            await ShowErrorAsync(Loc.Get("Str_RollbackError"), details);
        }
        finally
        {
            _isRollingBack = false;
            RollbackButtonState(true);
        }
    }

    private void SandboxToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_sandbox.IsWatching)
        {
            var confirm = MessageBox.Show(
                Loc.Get("Str_SandboxStopConfirmBody"),
                Loc.Get("Str_SandboxStopConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                UpdateSandboxGuardUi();
                return;
            }

            _sandbox.Stop();
            SandboxGuardPreferences.Save(null, false);
            UpdateSandboxGuardUi();
            StatusText.Text = Loc.Get("Str_SandboxGuardDisabled");
            return;
        }

        if (!_license.CanUseAutoSandbox())
        {
            MessageBox.Show(
                Loc.Get("Str_PremiumFeatureBody"),
                Loc.Get("Str_PremiumFeature"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            UpdateSandboxGuardUi();
            return;
        }

        RefreshSandboxVirtualizationCapability();
        if (!_sandboxVirtualizationCapability!.IsSupported)
        {
            UpdateSandboxGuardUi();
            ShowSandboxCapabilityBlockedStatus();
            return;
        }

        if (!TryEnableSandboxGuardWithFolderPicker())
        {
            UpdateSandboxGuardUi();
            StatusText.Text = Loc.Get("Str_SandboxGuardOffSelectFolder");
        }
    }

    private bool TryEnableSandboxGuardWithFolderPicker()
    {
        if (!FolderPickerDialog.TryPickFolder(this, out var selectedPath) || selectedPath == null)
        {
            return false;
        }

        try
        {
            var startResult = _sandbox.TryStart(selectedPath);
            if (startResult.Success)
            {
                SandboxGuardPreferences.Save(selectedPath, true);
                UpdateSandboxGuardUi();
                StatusText.Text = FormatSandboxGuardingStatus(selectedPath);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(startResult.StatusResourceKey))
            {
                UpdateSandboxGuardUi();
                StatusText.Text = Loc.Get(startResult.StatusResourceKey);
                return false;
            }

            var details = startResult.DiagnosticMessage ?? Loc.Get("Str_SandboxGuardStartFailed");
            MessageBox.Show(
                Loc.Format("Str_SandboxGuardStartFailed", selectedPath, details),
                Loc.Get("Str_SandboxGuardDialogTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            var details = ExceptionDisplayFormatter.Format(ex);
            MessageBox.Show(
                $"{Loc.Format("Str_SandboxGuardStartFailed", selectedPath, string.Empty).TrimEnd()}{Environment.NewLine}{Environment.NewLine}{details}",
                Loc.Get("Str_SandboxGuardDialogTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private async void PurchaseStorePremium_Click(object sender, RoutedEventArgs e)
    {
        ClearLicenseFailureUi();
        SetLicenseInputEnabled(false);
        StatusText.Text = Loc.Get("Str_ValidatingLicense");

        try
        {
            var result = await _license.RequestStorePurchaseAsync();
            if (result.Success && _license.IsPremium)
            {
                ShowLicenseSuccessUi();
            }
            else
            {
                RefreshLicenseUi();
                if (!string.IsNullOrWhiteSpace(result.StatusMessage))
                {
                    LicenseErrorText.Text = result.StatusMessage;
                    LicenseErrorPanel.Visibility = Visibility.Visible;
                    LicenseExpander.IsExpanded = true;
                }
            }
        }
        catch (Exception)
        {
            RefreshLicenseUi();
            ShowLicenseFailureUi();
        }
        finally
        {
            SetLicenseInputEnabled(true);
        }
    }

    private void ConfigureLicensePurchaseUi()
    {
        if (_license is null)
        {
            return;
        }

        if (_license.UsesMicrosoftStore)
        {
            LicenseExpander.Header = Loc.Get("Str_UpgradePremiumStore");
            LicenseKeyBox.Visibility = Visibility.Collapsed;
            ValidateLicenseButton.Visibility = Visibility.Collapsed;
            PurchaseStorePremiumButton.Visibility = Visibility.Visible;
            return;
        }

        LicenseExpander.Header = Loc.Get("Str_ActivatePremiumLicense");
        LicenseKeyBox.Visibility = Visibility.Visible;
        ValidateLicenseButton.Visibility = Visibility.Visible;
        PurchaseStorePremiumButton.Visibility = Visibility.Collapsed;
    }

    private async void ActivateLicense_Click(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            await ShowInfoAsync(Loc.Get("Str_License"), Loc.Get("Str_EnterLicenseKeyMessage"));
            LicenseKeyBox.Focus();
            Keyboard.Focus(LicenseKeyBox);
            return;
        }

        ClearLicenseFailureUi();
        SetLicenseInputEnabled(false);
        StatusText.Text = Loc.Get("Str_ValidatingLicense");

        try
        {
            var result = await _license.ActivateLicenseAsync(key);
            if (result.Success && _license.IsPremium)
            {
                ShowLicenseSuccessUi();
            }
            else
            {
                RefreshLicenseUi();
                ShowLicenseFailureUi();
            }
        }
        catch (Exception)
        {
            RefreshLicenseUi();
            ShowLicenseFailureUi();
        }
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        LicenseKeyBox.Text = string.Empty;
        LicenseKeyBox.IsEnabled = true;
        ValidateLicenseButton.IsEnabled = true;
        LicenseKeyBox.Focus();
        Keyboard.Focus(LicenseKeyBox);
        ClearLicenseFailureUi();
        StatusText.Text = Loc.Get("Str_StatusReady");
        BtnRetry.Visibility = Visibility.Collapsed;
    }

    private void ShowLicenseFailureUi()
    {
        _isLicenseFailureVisible = true;
        LicenseExpander.Visibility = Visibility.Visible;
        LicenseExpander.IsExpanded = true;
        LicenseErrorText.Text = Loc.Get("Str_LicenseErrorOneLine");
        LicenseErrorPanel.Visibility = Visibility.Visible;
        BtnRetry.Visibility = Visibility.Visible;
        SetLicenseInputEnabled(true);
    }

    private void ClearLicenseFailureUi()
    {
        _isLicenseFailureVisible = false;
        LicenseErrorPanel.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
    }

    private void ShowLicenseSuccessUi()
    {
        ClearLicenseFailureUi();
        SetLicenseInputEnabled(true);
        RefreshLicenseUi();
        ApplyPremiumSuccessStatus(Loc.Get("Str_PremiumActivatedThanks"));
        EnsureTimelineInteractive();
    }

    private void ApplyPremiumSuccessStatus(string message)
    {
        ResetStatusTextStyle();
        StatusText.Text = message;
    }

    private void ResetStatusTextStyle()
    {
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryForegroundBrush");
    }

    private void SetLicenseInputEnabled(bool enabled)
    {
        if (_license?.UsesMicrosoftStore == true)
        {
            PurchaseStorePremiumButton.IsEnabled = enabled;
            return;
        }

        LicenseKeyBox.IsEnabled = enabled;
        ValidateLicenseButton.IsEnabled = enabled;
    }

    private void RefreshLicenseUi()
    {
        ConfigureLicensePurchaseUi();
        if (_license.IsPremium)
        {
            LicenseBadge.Text = Loc.Get("Str_LicenseBadgePremium");
            LicenseBadge.Foreground = new SolidColorBrush(Colors.White);
            LicenseBadgeBorder.Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(184, 134, 11),
                System.Windows.Media.Color.FromRgb(255, 215, 64),
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
            LicenseBadgeBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 165, 32));
            LicenseExpander.Visibility = Visibility.Collapsed;
            LicenseExpander.IsExpanded = false;
            LicenseKeyBox.Text = string.Empty;
        }
        else
        {
            LicenseBadge.Text = Loc.Get("Str_LicenseBadgeFree");
            LicenseBadge.Foreground = (System.Windows.Media.Brush)FindResource("SubtleForegroundBrush");
            LicenseBadgeBorder.Background = (System.Windows.Media.Brush)FindResource("CardElevatedBrush");
            LicenseBadgeBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
            LicenseExpander.Visibility = Visibility.Visible;
        }

        UpdateSandboxGuardUi();
        ShowSandboxCapabilityBlockedStatus();
    }

    private void TakeSnapshotButtonState(bool enabled)
    {
        TakeSnapshotButton.IsEnabled = enabled && !_isRollingBack;
        TimelineSlider.IsEnabled = enabled && _snapshots.Count > 0;
    }

    private void RollbackButtonState(bool enabled)
    {
        RollbackButton.IsEnabled = enabled;
        TakeSnapshotButton.IsEnabled = enabled && !_isRollingBack;
    }

    private static Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = string.IsNullOrWhiteSpace(message)
                ? "No detailed error message was provided by the runtime."
                : message,
            CloseButtonText = Loc.Get("Str_Ok")
        };
        return dialog.ShowAsync();
    }

    private static Task ShowInfoAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = string.IsNullOrWhiteSpace(message)
                ? "No additional details were provided."
                : message,
            CloseButtonText = Loc.Get("Str_Ok")
        };
        return dialog.ShowAsync();
    }
}
