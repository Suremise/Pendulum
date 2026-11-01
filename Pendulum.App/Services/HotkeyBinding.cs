namespace Pendulum.App.Services;

/// What holds the system-wide shortcut: HotkeyManager in the app (RegisterHotKey on its own
/// window), a fake in tests, so no test registers a real hotkey.
internal interface IHotkeyRegistrar
{
    /// Registers <paramref name="gesture"/> in place of any held now; false if it can't be had
    /// (another app or Windows has it, or it isn't a valid shortcut).
    bool Register(string gesture);

    void Unregister();
}

/// What applying the Quick Reminder shortcut setting did.
internal enum HotkeyOutcome
{
    /// The shortcut is held (newly, or it already was).
    Registered,
    /// The setting is off: no shortcut.
    Off,
    /// The new shortcut is taken; the one held before it is held again.
    TakenKeptPrevious,
    /// The shortcut is taken and there was none before it (at startup): none is held.
    TakenNone,
    /// The new shortcut isn't one that can be used (blank, or not a modifier plus a key); the one
    /// held before it is held again.
    InvalidKeptPrevious,
    /// The shortcut isn't one that can be used and there was none before it: none is held.
    InvalidNone,
}

/// Holds the Quick Reminder shortcut, and never leaves the user without one silently: when a new
/// shortcut can't be registered, the one before it is registered again, and the caller says why.
internal sealed class HotkeyBinding
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly Func<string, bool> _isValid;

    /// <param name="isValid">Whether a gesture is one RegisterHotKey could take at all (a
    /// modifier plus a key); HotkeyManager's parser in the app.</param>
    public HotkeyBinding(IHotkeyRegistrar registrar, Func<string, bool>? isValid = null)
    {
        _registrar = registrar;
        _isValid = isValid ?? (g => HotkeyManager.TryParseGesture(g, out _, out _));
    }

    /// The shortcut held now, or null.
    public string? Active { get; private set; }

    public HotkeyOutcome Apply(bool enabled, string? gesture)
    {
        if (!enabled)
        {
            _registrar.Unregister();
            Active = null;
            return HotkeyOutcome.Off;
        }

        if (Active is not null && string.Equals(gesture, Active, StringComparison.OrdinalIgnoreCase))
            return HotkeyOutcome.Registered;

        var previous = Active;
        var valid = !string.IsNullOrWhiteSpace(gesture) && _isValid(gesture);
        if (valid && _registrar.Register(gesture!))
        {
            Active = gesture;
            return HotkeyOutcome.Registered;
        }

        // Register lets go of the one held before trying; an invalid gesture is never tried, so the
        // previous one is still held, and registering it again is harmless.
        if (previous is not null && _registrar.Register(previous))
        {
            Active = previous;
            return valid ? HotkeyOutcome.TakenKeptPrevious : HotkeyOutcome.InvalidKeptPrevious;
        }

        Active = null;
        return valid ? HotkeyOutcome.TakenNone : HotkeyOutcome.InvalidNone;
    }
}
