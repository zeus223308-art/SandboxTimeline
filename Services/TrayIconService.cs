using System.Drawing;
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
    private const string EmbeddedIconPackUri = "pack://application:,,,/assets/icon.ico";

    private readonly MainWindow _mainWindow;
    private HwndSource? _hwndSource;
    private Icon? _trayIcon;
    private IntPtr _iconHandle;
    private bool _added;
    private bool _disposed;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        CreateMessageWindow();
        AddTrayIcon();
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
            throw new InvalidOperationException("Failed to add system tray icon.");
        }

        _added = true;
    }

    private IntPtr LoadTrayIconHandle()
    {
        _trayIcon = LoadEmbeddedTrayIcon() ?? LoadFileTrayIcon();
        if (_trayIcon == null)
        {
            throw new InvalidOperationException(
                $"Tray icon could not be loaded from embedded resource ({EmbeddedIconPackUri}) or assets\\icon.ico.");
        }

        return _trayIcon.Handle;
    }

    private static Icon? LoadEmbeddedTrayIcon()
    {
        try
        {
            var resourceStream = System.Windows.Application.GetResourceStream(new Uri(EmbeddedIconPackUri, UriKind.Absolute));
            if (resourceStream?.Stream == null)
            {
                return null;
            }

            using (resourceStream.Stream)
            {
                return new Icon(resourceStream.Stream);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Embedded tray icon load failed.", ex);
            return null;
        }
    }

    private static Icon? LoadFileTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico");
        if (!File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            return new Icon(iconPath);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"File tray icon load failed for '{iconPath}'.", ex);
            return null;
        }
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
