using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class EditorViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 6, 3, 10, 0, 0);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-editor-tests-" + Guid.NewGuid().ToString("N"));
    public EditorViewModelTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }
    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static EditorViewModel Editor(TriggerTimer target, bool isNew = false, bool use24 = false) =>
        new(target, isNew, use24, ["bell.wav"], "teal");

    private static void Bind() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    [Theory]
    [InlineData(false, 0, "12", 0)]
    [InlineData(false, 12, "12", 1)]
    [InlineData(false, 23, "11", 1)]
    [InlineData(true, 0, "00", 0)]
    [InlineData(true, 23, "23", 1)]
    public void The_time_round_trips_in_both_clock_formats(bool use24, int hour, string shown, int amPm)
    {
        var at = Now.Date.AddHours(hour).AddMinutes(14).AddSeconds(15);
        var vm = Editor(new TriggerTimer { TriggerAt = at }, use24: use24);
        Assert.Equal(shown, vm.Hour);
        Assert.Equal(amPm, vm.AmPmIndex);
        Assert.Equal(at, vm.ScheduledAt(Now));
    }

    [Fact]
    public void A_new_speaking_mode_defaults_the_phrase_without_overwriting_it_or_an_existing_reminder()
    {
        var target = new TriggerTimer { Name = "Dentist", TriggerAt = Now.AddHours(1) };
        var vm = Editor(target, isNew: true);
        vm.Mode = AlertMode.SoundAndSpeech;
        Assert.Equal("Dentist", vm.Phrase);
        vm.Name = "Tea";
        vm.Mode = AlertMode.SpeechOnly;
        Assert.Equal("Dentist", vm.Phrase);
        var existing = Editor(target);
        existing.Mode = AlertMode.SpeechOnly;
        Assert.Empty(existing.Phrase);
        Assert.Equal("Dentist", target.Name);
        Assert.Equal(AlertMode.SoundOnly, target.Mode);
    }

    [Fact]
    public void Whole_second_controls_keep_an_unchanged_relative_deadlines_precision()
    {
        var at = Now.AddMinutes(5).AddTicks(1234567);
        var target = new TriggerTimer();
        var due = DateTime.SpecifyKind(at, DateTimeKind.Utc);
        target.SetDue(at, due);
        var vm = Editor(target, use24: true);
        TriggerReconciler.ApplyEdit(target, vm.CreateDraft(Now), Now, DateTime.SpecifyKind(Now, DateTimeKind.Utc));
        Assert.Equal(at, target.TriggerAt);
        Assert.Equal(due, target.DueUtc);
    }

    [Fact]
    public void Loading_a_new_speaking_reminder_does_not_fill_an_initially_empty_phrase()
    {
        UiThread.Invoke(() =>
        {
            AppServices.Instance.UseSettingsForTests(new AppSettings());
            var target = new TriggerTimer { Name = "Dentist", TriggerAt = Now.AddHours(1), Mode = AlertMode.SpeechOnly };
            var window = new TriggerEditWindow(target, isNew: true);
            try
            {
                Bind();
                Assert.Equal(AlertMode.SpeechOnly, window.Editor.Mode);
                Assert.Equal(AlertMode.SpeechOnly, window.AlertTypeBox.SelectedValue);
                Assert.Empty(window.Editor.Phrase);
                Assert.Empty(window.PhraseBox.Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void The_unshown_view_binds_edits_and_dependent_states_in_both_directions()
    {
        UiThread.Invoke(() =>
        {
            AppServices.Instance.UseSettingsForTests(new AppSettings { Use24HourTime = true });
            var target = new TriggerTimer { Name = "Dentist", TriggerAt = Now.Date.AddHours(13).AddMinutes(14), Enabled = false };
            var window = new TriggerEditWindow(target, isNew: true);
            try
            {
                Bind();
                Assert.Same(window.Editor, window.DataContext);
                Assert.Equal("13", window.HourBox.SelectedItem);
                Assert.Equal(Visibility.Collapsed, window.AmPmBox.Visibility);
                Assert.Equal(Visibility.Visible, window.DisabledHint.Visibility);
                window.NameBox.Text = "Tea";
                Bind();
                Assert.Equal("Tea", window.Editor.Name);
                window.Editor.Mode = AlertMode.SpeechOnly;
                Bind();
                Assert.True(window.PhraseBox.IsEnabled);
                Assert.False(window.SoundBox.IsEnabled);
                Assert.False(window.VolumeSlider.IsEnabled);
                Assert.Equal(0.5, window.SoundLabel.Opacity);
                Assert.Equal(1, window.PhraseLabel.Opacity);
                Assert.Equal("Tea", window.PhraseBox.Text);
                window.Editor.Hour = "23";
                Bind();
                Assert.Equal("23", window.HourBox.SelectedItem);
                window.Editor.Enabled = true;
                Bind();
                Assert.Equal(Visibility.Collapsed, window.DisabledHint.Visibility);
                window.VolumeSlider.Value = 62.9;
                Bind();
                Assert.Equal(62, window.Editor.VolumePercent);
                Assert.Equal("62", window.VolumeValueText.Text);
                var chosen = window.Editor.Colour == "teal" ? "blue" : "teal";
                window.Editor.ChooseColour(chosen);
                Bind();
                Assert.Equal(chosen, (string)window.ColourChoices.Children.OfType<RadioButton>().Single(r => r.IsChecked == true).Tag);
                Assert.Equal("Dentist", target.Name);
            }
            finally { window.Close(); }
            Assert.Empty(Application.Current.Windows);
        });
    }
}
