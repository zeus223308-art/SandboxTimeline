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
    private const string PrimaryPackIconUri = "pack://application:,,,/assets/icon.ico";

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
        try
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
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Tray message window creation failed.", ex);
            _hwndSource = null;
        }
    }

    private void AddTrayIcon()
    {
        try
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
        catch (Exception ex)
        {
            StartupDiagnostics.Log("AddTrayIcon failed; tray bypassed.", ex);
        }
    }

    private IntPtr LoadTrayIconHandle()
    {
        try
        {
            _trayIcon?.Dispose();
            _trayIcon = LoadTrayIconWithFallback();
            return _trayIcon?.Handle ?? IntPtr.Zero;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("LoadTrayIconHandle failed; tray bypassed.", ex);
            return IntPtr.Zero;
        }
    }

    private static Icon LoadTrayIconWithFallback()
    {
        try
        {
            var resourceStream = System.Windows.Application.GetResourceStream(new Uri(PrimaryPackIconUri));
            if (resourceStream?.Stream != null)
            {
                using var iconStream = resourceStream.Stream;
                return new Icon(iconStream);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Pack URI tray icon load failed; using fallback icon.", ex);
        }

        try
        {
            var executablePath = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var associatedIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (associatedIcon != null)
                {
                    return (Icon)associatedIcon.Clone();
                }
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("ExtractAssociatedIcon tray fallback failed.", ex);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
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
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("TrayWndProc handler failed.", ex);
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        try
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
        catch (Exception ex)
        {
            StartupDiagnostics.Log("ShowContextMenu failed.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
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
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Tray icon removal failed during dispose.", ex);
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
