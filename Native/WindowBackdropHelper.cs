using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SandboxTimeline;

public static class WindowBackdropHelper
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainwindow = 2;

    public static void TryApplyMica(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        void ApplyBackdrop()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var backdrop = DwmsbtMainwindow;
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }

        if (window.IsLoaded)
        {
            ApplyBackdrop();
        }
        else
        {
            window.SourceInitialized += (_, _) => ApplyBackdrop();
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
