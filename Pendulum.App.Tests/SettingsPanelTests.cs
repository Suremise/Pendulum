using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// The Settings screen with a v1.1.0 settings file, read the way a screen reader reads it: through
/// the UI Automation providers WPF gives each control (its AutomationPeers).
///
/// No window is ever shown. The panel is built on the tests' UI thread and laid out in memory, so
/// nothing can take the foreground or keyboard focus from the user's apps, and there is no
/// keyboard or mouse input. Files stay in the test's temp folder.
[Collection(nameof(SettingsScreen))]
public sealed class SettingsPanelTests : IDisposable
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.1.0", "settings.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly object? _runValueBefore = ReadRunValue();

    public SettingsPanelTests()
    {
        // A stand-in for the folder beside the exe, with the files the v1.1.0 settings name, so the
        // screen finds them as it would on that user's PC.
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
        foreach (var file in new[]
                 {
                     Path.Combine(AppPaths.SoundsDirectory, "bell.wav"),
                     Path.Combine(AppPaths.SoundsDirectory, "chime.wav"),
                     Path.Combine(AppPaths.WhisperModelsDirectory, "ggml-base.en.bin"),
                     Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe"),
                     Path.Combine(AppPaths.PiperModelsDirectory, "en_GB-jenny_dioco-medium.onnx"),
                     Path.Combine(AppPaths.PiperModelsDirectory, "en_GB-jenny_dioco-medium.onnx.json"),
                 })
            File.WriteAllBytes(file, []);
        File.Copy(FixturePath, AppPaths.SettingsFile);
    }

    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Nothing may reach the user's desktop or registry: no window was ever shown, and the
        // Run key ("Start with Windows" writes it when changed) is as it was.
        UiThread.Invoke(() => Assert.Empty(Application.Current.Windows));
        Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
        Assert.Equal(_runValueBefore, ReadRunValue());
    }

    [Fact]
    public void A_v1_1_0_settings_file_shows_every_value_and_saves_back_unchanged()
    {
        var settings = new SettingsRepository().Load();
        UiThread.Invoke(() =>
        {
            var panel = Build(settings);

            Select(panel, "General");
            Assert.Equal(ToggleState.On, Toggle(panel, "LaunchOnStartupBox"));
            Assert.Equal(ToggleState.Off, Toggle(panel, "StartMinimizedBox"));
            Assert.Equal("Dark", Value(panel, "ThemeBox"));
            Assert.Equal("24-hour (14:30)", Value(panel, "TimeFormatBox"));
            Assert.Equal(ToggleState.Off, Toggle(panel, "UpdateCheckBox"));

            Select(panel, "Alerts");
            Assert.Equal("bell.wav", Value(panel, "DefaultSoundBox"));
            Assert.Equal("10", Value(panel, "SnoozeBox"));
            Assert.Equal(ToggleState.Off, Toggle(panel, "RepeatBox"));
            Assert.Equal("15", Value(panel, "AutoResolveBox"));
            Assert.True(Peer(panel, "AutoResolveBox").IsEnabled());

            Select(panel, "Spoken alerts");
            Assert.Equal("Piper", Value(panel, "TtsEngineBox"));
            Assert.Equal("en_GB-jenny_dioco-medium.onnx", Value(panel, "PiperVoiceBox"));
            Assert.True(IsShown(panel, "PiperVoiceBox"));
            Assert.False(IsShown(panel, "WindowsVoiceBox"), "the Windows voice list is hidden while Piper is chosen");
            Assert.Equal(2, Range(panel, "RateSlider"));
            Assert.Equal(80, Range(panel, "VolumeSlider"));

            Select(panel, "Quick Reminder");
            Assert.Equal(ToggleState.On, Toggle(panel, "HotkeyEnabledBox"));
            Assert.Equal("Shortcut: Ctrl+Alt+P", Peer(panel, "HotkeyText").GetName());
            Assert.Equal("Whisper", Value(panel, "SttEngineBox"));
            Assert.Equal("ggml-base.en.bin", Value(panel, "WhisperModelBox"));
            Assert.True(IsShown(panel, "WhisperModelBox"));
            Assert.Equal(ToggleState.On, Toggle(panel, "AutoListenBox"));

            Select(panel, "Backup and data");
            Assert.Equal("30", Value(panel, "AutoDeleteBox"));
        });

        // Saved back, it is the same file: every key, every value.
        new SettingsRepository().Save(settings);
        var expected = JsonNode.Parse(File.ReadAllText(FixturePath));
        var actual = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Saved settings differ from v1.1.0's:\n{actual}");
    }

    [Fact]
    public void Each_section_is_named_and_only_one_shows_at_a_time()
    {
        var settings = new SettingsRepository().Load();
        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            Assert.Equal("Settings sections", Peer(panel, "SectionList").GetName());

            var sections = new[] { ("General", "GeneralSection"), ("Alerts", "AlertsSection"), ("Spoken alerts", "VoiceSection"),
                                   ("Quick Reminder", "QuickSection"), ("Backup and data", "DataSection") };
            foreach (var (name, section) in sections)
            {
                Select(panel, name);
                foreach (var (_, other) in sections)
                    Assert.Equal(other == section, IsShown(panel, other));
            }
        });
    }

    [Fact]
    public void The_section_list_works_from_the_keyboard()
    {
        // Checked from its properties, with no keyboard focus or key presses (they can take the
        // foreground from the user's apps). A single-selection ListBox moves with the arrow keys;
        // Tab enters it once and goes on to the section shown.
        var settings = new SettingsRepository().Load();
        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            var list = (ListBox)panel.FindName("SectionList");
            Assert.Equal(SelectionMode.Single, list.SelectionMode);
            Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(list));
            foreach (ListBoxItem item in list.Items)
            {
                Assert.True(item.Focusable && item.IsEnabled);
                Assert.True(item.IsTabStop);
            }
            var items = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren();
            Assert.Equal(new[] { "General", "Alerts", "Spoken alerts", "Quick Reminder", "Backup and data" }, items.Select(p => p.GetName()));
            Assert.All(items, p => Assert.NotNull(p.GetPattern(PatternInterface.SelectionItem)));
        });
    }

    [Fact]
    public void Settings_that_depend_on_another_follow_it()
    {
        var settings = new SettingsRepository().Load();
        UiThread.Invoke(() =>
        {
            var panel = Build(settings);

            // Keep alerting on: the time to dismiss it after doesn't apply, and is disabled.
            Assert.True(Peer(panel, "AutoResolveBox").IsEnabled());
            settings.RepeatAlertUntilDismissed = true;
            Assert.False(Peer(panel, "AutoResolveBox").IsEnabled());

            // The shortcut off: changing it is disabled.
            var change = FindByName(panel, "Change the shortcut");
            Assert.True(change.IsEnabled());
            settings.QuickAddHotkeyEnabled = false;
            Assert.False(change.IsEnabled());

            // The Windows speech engine: its voices show, Piper's don't.
            settings.TextToSpeechEngine = TextToSpeechEngine.WindowsSpeechSynthesis;
            Layout(panel);
            Assert.True(IsShown(panel, "WindowsVoiceBox", withinSection: true));
            Assert.False(IsShown(panel, "PiperVoiceBox", withinSection: true));

            // Whisper's model shows only with Whisper.
            settings.SpeechToTextEngine = SpeechToTextEngine.WindowsSpeechRecognition;
            Layout(panel);
            Assert.False(IsShown(panel, "WhisperModelBox", withinSection: true));
        });
    }

    // --- The panel and its UI Automation providers ------------------------------------------

    private static SettingsPanel Build(AppSettings settings)
    {
        AppServices.Instance.UseSettingsForTests(settings);
        var panel = new SettingsPanel { Width = 1100, Height = 820 };
        Layout(panel);
        return panel;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }

    /// Chooses a section the way a screen reader does, through the item's selection provider.
    private static void Select(SettingsPanel panel, string section)
    {
        // The list's own provider gives each item's: it's the one a screen reader talks to.
        var list = (ListBox)panel.FindName("SectionList");
        var item = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren().Single(p => p.GetName() == section);
        var selection = (ISelectionItemProvider)item.GetPattern(PatternInterface.SelectionItem);
        selection.Select();
        Layout(panel);
        Assert.True(selection.IsSelected);
    }

    private static AutomationPeer Peer(SettingsPanel panel, string name) =>
        UIElementAutomationPeer.CreatePeerForElement((UIElement)(panel.FindName(name) ?? throw new InvalidOperationException($"No element {name}")));

    private static AutomationPeer FindByName(SettingsPanel panel, string automationName)
    {
        var element = Descendants(panel).OfType<UIElement>().FirstOrDefault(e => AutomationProperties.GetName(e) == automationName)
                      ?? throw new InvalidOperationException($"No element named {automationName}");
        return UIElementAutomationPeer.CreatePeerForElement(element);
    }

    private static ToggleState Toggle(SettingsPanel panel, string name) =>
        ((IToggleProvider)Peer(panel, name).GetPattern(PatternInterface.Toggle)).ToggleState;

    /// A text box's text, or a combo box's chosen item as a screen reader names it.
    private static string? Value(SettingsPanel panel, string name)
    {
        var peer = Peer(panel, name);
        if (peer.GetPattern(PatternInterface.Value) is IValueProvider value)
            return value.Value;
        var chosen = peer.GetChildren()?.FirstOrDefault(p => p.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider { IsSelected: true });
        return chosen?.GetName();
    }

    private static double Range(SettingsPanel panel, string name) =>
        ((IRangeValueProvider)Peer(panel, name).GetPattern(PatternInterface.RangeValue)).Value;

    /// Shown: it and every element above it visible, up to the panel (or to its section).
    private static bool IsShown(SettingsPanel panel, string name, bool withinSection = false)
    {
        DependencyObject? node = (DependencyObject)panel.FindName(name);
        while (node is not null && node != panel)
        {
            if (withinSection && node is FrameworkElement { Name: var n } && n.EndsWith("Section", StringComparison.Ordinal))
                return true;
            if (node is UIElement { Visibility: not Visibility.Visible })
                return false;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return node == panel;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }

    private static object? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue("Pendulum");
    }
}

/// The tests share one AppServices and one AppPaths, so they run one at a time.
[CollectionDefinition(nameof(SettingsScreen), DisableParallelization = true)]
public sealed class SettingsScreen;
