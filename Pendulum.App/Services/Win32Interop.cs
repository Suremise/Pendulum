using System.Runtime.InteropServices;

namespace Pendulum.App.Services;

internal static class Win32Interop
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// Reasserts the always-on-top flag. Called periodically for windows
    /// that must stay visible over other topmost apps/games.
    public static void ForceTopmost(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
