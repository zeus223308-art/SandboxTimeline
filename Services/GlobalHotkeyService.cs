using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SandboxTimeline;

/// <summary>
/// Lightweight global hotkey host using RegisterHotKey (no polling loops).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private HwndSource? _messageSource;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        var parameters = new HwndSourceParameters("SandboxTimelineHotkeySink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = IntPtr.Zero,
            UsesPerPixelOpacity = false
        };

        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WndProc);

        if (!Win32Hotkey.RegisterHotKey(
                _messageSource.Handle,
                Win32Hotkey.HotkeyId,
                GlobalHotkeyConfiguration.DefaultModifiers,
                GlobalHotkeyConfiguration.DefaultVirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            _messageSource.RemoveHook(WndProc);
            _messageSource.Dispose();
            _messageSource = null;
            throw new InvalidOperationException(
                $"Failed to register {GlobalHotkeyConfiguration.DisplayName} global hotkey (Win32 error {error}). Another app may already own this combination.");
        }

        _registered = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Hotkey.WmHotkey && wParam.ToInt32() == Win32Hotkey.HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Pause()
    {
        if (!_registered || _messageSource == null)
        {
            return;
        }

        Win32Hotkey.UnregisterHotKey(_messageSource.Handle, Win32Hotkey.HotkeyId);
        _registered = false;
    }

    public void Resume()
    {
        if (_registered || _messageSource == null)
        {
            return;
        }

        if (!Win32Hotkey.RegisterHotKey(
                _messageSource.Handle,
                Win32Hotkey.HotkeyId,
                GlobalHotkeyConfiguration.DefaultModifiers,
                GlobalHotkeyConfiguration.DefaultVirtualKey))
        {
            return;
        }

        _registered = true;
    }

    public void Dispose()
    {
        if (_messageSource != null)
        {
            if (_registered)
            {
                Win32Hotkey.UnregisterHotKey(_messageSource.Handle, Win32Hotkey.HotkeyId);
                _registered = false;
            }

            _messageSource.RemoveHook(WndProc);
            _messageSource.Dispose();
            _messageSource = null;
        }
    }
}
