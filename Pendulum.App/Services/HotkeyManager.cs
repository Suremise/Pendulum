using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Pendulum.App.Services;

/// Registers a single system-wide hotkey (e.g. "Ctrl+Shift+R") against a window and raises
/// <see cref="Pressed"/> when it fires. The window is never shown or activated by this class —
/// its handle is only used as the WM_HOTKEY message target, so it works even while Pendulum's
/// main window is hidden in the tray.
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xA5B1;

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    public void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    public void Detach()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    /// Attempts to (re-)register the given gesture, replacing any hotkey currently held.
    /// Returns false if the gesture is invalid or already claimed by another application.
    public bool Register(string gesture)
    {
        Unregister();

        if (_source is null || !TryParseGesture(gesture, out var modifiers, out var key))
            return false;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = Win32Interop.RegisterHotkey(_source.Handle, HotkeyId, ToWin32Modifiers(modifiers), virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
            Win32Interop.UnregisterHotkey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static Win32Interop.HotkeyModifiers ToWin32Modifiers(ModifierKeys modifiers)
    {
        var result = Win32Interop.HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= Win32Interop.HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= Win32Interop.HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= Win32Interop.HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= Win32Interop.HotkeyModifiers.Win;
        return result;
    }

    /// Parses a "Ctrl+Shift+R"-style gesture string. Requires at least one modifier plus
    /// exactly one non-modifier key, matching what RegisterHotKey itself requires.
    public static bool TryParseGesture(string? gesture, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        if (modifiers == ModifierKeys.None)
            return false;

        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    public static string FormatGesture(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public void Dispose() => Detach();
}
