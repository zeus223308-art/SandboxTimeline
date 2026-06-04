using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SandboxTimeline;

/// <summary>
/// Native Shell_NotifyIcon tray integration (no WinForms, no polling).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly string[] EmbeddedIconPackUris =
    [
        "pack://application:,,,/assets/icon.ico",
        "pack://application:,,,/SandboxTimeline;component/assets/icon.ico"
    ];

    private readonly MainWindow _mainWindow;
    private HwndSource? _hwndSource;
    private Icon? _trayIcon;
    private IntPtr _iconHandle;
    private bool _added;
    private bool _disposed;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        try
        {
            CreateMessageWindow();
            AddTrayIcon();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Tray icon service initialization failed; continuing without tray.", ex);
        }
    }

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("SandboxTimelineTraySink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = IntPtr.Zero
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(TrayWndProc);
    }

    private void AddTrayIcon()
    {
        if (_hwndSource == null)
        {
            return;
        }

        _iconHandle = LoadTrayIconHandle();
        if (_iconHandle == IntPtr.Zero)
        {
            StartupDiagnostics.Log("Tray icon handle is zero; skipping Shell_NotifyIcon registration.");
            return;
        }

        var data = new ShellTrayIcon.NotifyIconData
        {
            cbSize = Marshal.SizeOf<ShellTrayIcon.NotifyIconData>(),
            hWnd = _hwndSource.Handle,
            uID = 1,
            uFlags = ShellTrayIcon.NifMessage | ShellTrayIcon.NifIcon | ShellTrayIcon.NifTip,
            uCallbackMessage = ShellTrayIcon.TrayIconMessageId,
            hIcon = _iconHandle,
            szTip = "Sandbox Timeline"
        };

        if (!ShellTrayIcon.Shell_NotifyIcon(ShellTrayIcon.NimAdd, ref data))
        {
            StartupDiagnostics.Log("Shell_NotifyIcon(NIM_ADD) returned false; tray icon not shown.");
            return;
        }

        _added = true;
    }

    private IntPtr LoadTrayIconHandle()
    {
        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;

            _trayIcon =
                LoadEmbeddedTrayIcon()
                ?? LoadFileTrayIcon()
                ?? LoadExecutableAssociatedTrayIcon()
                ?? LoadSystemApplicationFallbackIcon();

            if (_trayIcon == null)
            {
                StartupDiagnostics.Log("All tray icon loaders returned null; tray will be unavailable.");
                return IntPtr.Zero;
            }

            return _trayIcon.Handle;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("LoadTrayIconHandle failed; attempting system fallback icon.", ex);

            try
            {
                _trayIcon?.Dispose();
                _trayIcon = LoadSystemApplicationFallbackIcon();
                return _trayIcon?.Handle ?? IntPtr.Zero;
            }
            catch (Exception fallbackEx)
            {
                StartupDiagnostics.Log("System fallback tray icon load failed.", fallbackEx);
                return IntPtr.Zero;
            }
        }
    }

    private static Icon? LoadEmbeddedTrayIcon()
    {
        foreach (var packUri in EmbeddedIconPackUris)
        {
            try
            {
                var resourceStream = System.Windows.Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
                if (resourceStream?.Stream == null)
                {
                    continue;
                }

                using (resourceStream.Stream)
                {
                    return new Icon(resourceStream.Stream);
                }
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Embedded tray icon load failed for '{packUri}'.", ex);
            }
        }

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var manifestName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));

            if (manifestName == null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(manifestName);
            return stream == null ? null : new Icon(stream);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Manifest resource tray icon load failed.", ex);
            return null;
        }
    }

    private static Icon? LoadFileTrayIcon()
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "icon.ico"),
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "assets", "icon.ico")
        };

        foreach (var iconPath in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(iconPath))
            {
                continue;
            }

            try
            {
                return new Icon(iconPath);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"File tray icon load failed for '{iconPath}'.", ex);
            }
        }

        return null;
    }

    private static Icon? LoadExecutableAssociatedTrayIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            var associated = Icon.ExtractAssociatedIcon(executablePath);
            return associated == null ? null : (Icon)associated.Clone();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Executable associated tray icon load failed.", ex);
            return null;
        }
    }

    private Icon? LoadSystemApplicationFallbackIcon()
    {
        StartupDiagnostics.Log("Using SystemIcons.Application as tray icon fallback.");
        return (Icon)SystemIcons.Application.Clone();
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ShellTrayIcon.TrayIconMessageId)
        {
            var mouseMsg = (int)lParam;
            if (mouseMsg == ShellTrayIcon.WmLButtonDblClk)
            {
                _mainWindow.Dispatcher.BeginInvoke(_mainWindow.ShowAndActivateTimeline);
                handled = true;
            }
            else if (mouseMsg == ShellTrayIcon.WmRButtonUp)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_hwndSource == null)
        {
            return;
        }

        var trayWindowHandle = _hwndSource.Handle;
        _mainWindow.Dispatcher.BeginInvoke(
            () => _mainWindow.ShowTrayContextMenuAtCursor(trayWindowHandle),
            DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added && _hwndSource != null)
        {
            var data = new ShellTrayIcon.NotifyIconData
            {
                cbSize = Marshal.SizeOf<ShellTrayIcon.NotifyIconData>(),
                hWnd = _hwndSource.Handle,
                uID = 1
            };
            ShellTrayIcon.Shell_NotifyIcon(ShellTrayIcon.NimDelete, ref data);
            _added = false;
        }

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
            _iconHandle = IntPtr.Zero;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(TrayWndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
