using System.Runtime.InteropServices;

namespace SandboxTimeline;

internal static class Win32Hotkey
{
    public const int WmHotkey = 0x0312;

    public const uint ModControl = 0x0002;
    public const uint ModWin = 0x0008;
    public const uint ModControlWin = ModControl | ModWin;
    public const uint VkZ = 0x5A;

    public const int HotkeyId = 0x5A4E;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
