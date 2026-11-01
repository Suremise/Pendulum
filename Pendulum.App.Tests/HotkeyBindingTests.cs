using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// Holding the Quick Reminder shortcut, against a fake registrar: no real hotkey is registered,
/// no window is made, and there's no keyboard input.
public sealed class HotkeyBindingTests
{
    /// Stands in for RegisterHotKey: shortcuts in <see cref="TakenElsewhere"/> belong to another
    /// app; registering one lets go of the shortcut held, as HotkeyManager does.
    private sealed class FakeRegistrar : IHotkeyRegistrar
    {
        public HashSet<string> TakenElsewhere { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Held { get; private set; }
        public List<string> Attempts { get; } = new();

        public bool Register(string gesture)
        {
            Attempts.Add(gesture);
            Held = null;
            if (TakenElsewhere.Contains(gesture))
                return false;
            Held = gesture;
            return true;
        }

        public void Unregister() => Held = null;
    }

    [Fact]
    public void A_free_shortcut_is_registered()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);

        Assert.Equal(HotkeyOutcome.Registered, binding.Apply(true, "Ctrl+Shift+R"));
        Assert.Equal("Ctrl+Shift+R", registrar.Held);
        Assert.Equal("Ctrl+Shift+R", binding.Active);
    }

    [Fact]
    public void A_taken_new_shortcut_gives_way_to_the_one_before_which_keeps_working()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");
        registrar.TakenElsewhere.Add("Ctrl+Alt+T");

        var outcome = binding.Apply(true, "Ctrl+Alt+T");

        Assert.Equal(HotkeyOutcome.TakenKeptPrevious, outcome);
        Assert.Equal("Ctrl+Shift+R", registrar.Held);
        Assert.Equal("Ctrl+Shift+R", binding.Active);
    }

    [Fact]
    public void A_saved_shortcut_taken_at_startup_leaves_none_held_and_says_so()
    {
        var registrar = new FakeRegistrar();
        registrar.TakenElsewhere.Add("Ctrl+Shift+R");
        var binding = new HotkeyBinding(registrar);

        Assert.Equal(HotkeyOutcome.TakenNone, binding.Apply(true, "Ctrl+Shift+R"));
        Assert.Null(registrar.Held);
        Assert.Null(binding.Active);
    }

    [Fact]
    public void Once_the_other_app_lets_go_choosing_it_again_works()
    {
        var registrar = new FakeRegistrar();
        registrar.TakenElsewhere.Add("Ctrl+Shift+R");
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");

        registrar.TakenElsewhere.Clear();
        binding.Apply(false, "Ctrl+Shift+R");

        Assert.Equal(HotkeyOutcome.Registered, binding.Apply(true, "Ctrl+Shift+R"));
        Assert.Equal("Ctrl+Shift+R", registrar.Held);
    }

    [Fact]
    public void Turning_it_off_lets_go_of_the_shortcut()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");

        Assert.Equal(HotkeyOutcome.Off, binding.Apply(false, "Ctrl+Shift+R"));
        Assert.Null(registrar.Held);
        Assert.Null(binding.Active);
    }

    [Fact]
    public void Applying_the_shortcut_already_held_doesnt_register_it_again()
    {
        // Settings put back to the kept shortcut raises a change too: that's a no-op.
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");

        Assert.Equal(HotkeyOutcome.Registered, binding.Apply(true, "ctrl+shift+r"));
        Assert.Single(registrar.Attempts);
    }

    [Fact]
    public void If_the_one_before_was_taken_meanwhile_too_none_is_held()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");
        registrar.TakenElsewhere.UnionWith(new[] { "Ctrl+Alt+T", "Ctrl+Shift+R" });

        Assert.Equal(HotkeyOutcome.TakenNone, binding.Apply(true, "Ctrl+Alt+T"));
        Assert.Null(binding.Active);
    }

    [Fact]
    public void An_invalid_shortcut_keeps_the_one_before_and_isnt_tried()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");

        Assert.Equal(HotkeyOutcome.InvalidKeptPrevious, binding.Apply(true, "R"));
        Assert.Equal("Ctrl+Shift+R", registrar.Held);
        Assert.Equal("Ctrl+Shift+R", binding.Active);
        Assert.DoesNotContain("R", registrar.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("R")]
    [InlineData("Ctrl+")]
    public void A_saved_shortcut_that_isnt_one_is_reported_not_taken_for_registered(string? gesture)
    {
        // Settings or a restored backup can hold null or junk: at startup that's no shortcut, said so.
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);

        var outcome = binding.Apply(true, gesture);

        Assert.Equal(HotkeyOutcome.InvalidNone, outcome);
        Assert.Null(binding.Active);
        Assert.Null(registrar.Held);
        Assert.Empty(registrar.Attempts);
    }

    [Fact]
    public void A_null_shortcut_from_settings_keeps_the_one_before()
    {
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);
        binding.Apply(true, "Ctrl+Shift+R");

        Assert.Equal(HotkeyOutcome.InvalidKeptPrevious, binding.Apply(true, null));
        Assert.Equal("Ctrl+Shift+R", registrar.Held);
    }

    [Fact]
    public void Nothing_held_isnt_mistaken_for_the_shortcut_already_held()
    {
        // With nothing held, a null shortcut must not count as "the same one, already held".
        var registrar = new FakeRegistrar();
        var binding = new HotkeyBinding(registrar);

        Assert.NotEqual(HotkeyOutcome.Registered, binding.Apply(true, null));
    }
}
