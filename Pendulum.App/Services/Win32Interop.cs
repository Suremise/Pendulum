using System.Runtime.InteropServices;

namespace Pendulum.App.Services;

internal static class Win32Interop
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_HOTKEY = 0x0312;

    [Flags]
    public enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// Registers a system-wide hotkey against a specific window, which then receives a
    /// WM_HOTKEY message (with wParam == id) whenever it's pressed. Returns false if the
    /// combination is already claimed by another app.
    public static bool RegisterHotkey(IntPtr hwnd, int id, HotkeyModifiers modifiers, uint virtualKey) =>
        RegisterHotKey(hwnd, id, (uint)modifiers, virtualKey);

    public static bool UnregisterHotkey(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// Forces a window to the foreground even when Pendulum's process didn't already own
    /// input focus (Windows normally refuses SetForegroundWindow in that case). Needed for
    /// the quick-add popup, which is summoned by a global hotkey while some other app has
    /// focus — without this trick the popup would appear on top but not actually receive
    /// keystrokes until the user clicked into it.
    public static void ForceForegroundWindow(IntPtr hwnd)
    {
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();

        if (foregroundThread != currentThread)
            AttachThreadInput(currentThread, foregroundThread, true);

        SetForegroundWindow(hwnd);

        if (foregroundThread != currentThread)
            AttachThreadInput(currentThread, foregroundThread, false);
    }

    /// Reasserts the always-on-top flag. Called periodically for windows
    /// that must stay visible over other topmost apps/games.
    public static void ForceTopmost(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// Marks the window as non-activating (WS_EX_NOACTIVATE): it can still show topmost
    /// and its buttons still receive clicks, but appearing (or being clicked) never steals
    /// foreground/input focus from whatever the user was already doing — e.g. a fullscreen
    /// game, where losing focus can drop held input like a held mouse button.
    public static void MakeNonActivating(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_NOACTIVATE));
    }
}
