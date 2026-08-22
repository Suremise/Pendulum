using System.Windows;
using Pendulum.App.Services;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class TriggerEditWindow : FluentWindow
{
    private static readonly (AlertMode Mode, string Label)[] AlertTypeOptions =
    {
        (AlertMode.SoundOnly, "Sound only"),
        (AlertMode.SoundAndSpeech, "Sound + speech"),
        (AlertMode.SpeechOnly, "Speech only"),
    };

    private readonly TriggerTimer _target;
    private readonly bool _use24Hour;
    private readonly bool _isNew;
    private bool _isLoading;
    private RecurrenceRule? _recurrence;

    public TriggerTimer? Result { get; private set; }

    public TriggerEditWindow(TriggerTimer target, bool isNew)
    {
        InitializeComponent();
        _target = target;
        _use24Hour = AppServices.Instance.Settings.Use24HourTime;
        _isNew = isNew;
        Title = isNew ? "New Reminder" : "Edit Reminder";
        EditorTitleBar.Title = Title;

        SetupTimeFormat();
        for (int m = 0; m < 60; m++)
            MinuteBox.Items.Add(m.ToString("00"));
        for (int s = 0; s < 60; s++)
            SecondBox.Items.Add(s.ToString("00"));
        AmPmBox.Items.Add("AM");
        AmPmBox.Items.Add("PM");

        foreach (var option in AlertTypeOptions)
            AlertTypeBox.Items.Add(option.Label);

        SoundBox.Items.Add("(None)");
        foreach (var f in AppServices.Instance.GetAvailableSoundFiles())
            SoundBox.Items.Add(f);

        LoadFrom(target);
    }

    private void SetupTimeFormat()
    {
        HourBox.Items.Clear();

        if (_use24Hour)
        {
            for (int h = 0; h <= 23; h++)
                HourBox.Items.Add(h.ToString("00"));

            AmPmBox.Visibility = Visibility.Collapsed;
            AmPmSpacerColumn.Width = new GridLength(0);
            AmPmColumn.Width = new GridLength(0);
        }
        else
        {
            for (int h = 1; h <= 12; h++)
                HourBox.Items.Add(h.ToString());

            AmPmBox.Visibility = Visibility.Visible;
            AmPmSpacerColumn.Width = new GridLength(8);
            AmPmColumn.Width = new GridLength(76);
        }
    }

    private void LoadFrom(TriggerTimer t)
    {
        _isLoading = true;

        NameBox.Text = t.Name;

        var dt = t.TriggerAt;
        DatePickerControl.SelectedDate = dt.Date;
        MinuteBox.SelectedItem = dt.Minute.ToString("00");
        SecondBox.SelectedItem = dt.Second.ToString("00");

        if (_use24Hour)
        {
            HourBox.SelectedItem = dt.Hour.ToString("00");
        }
        else
        {
            int hour12 = dt.Hour % 12;
            if (hour12 == 0) hour12 = 12;
            HourBox.SelectedItem = hour12.ToString();
            AmPmBox.SelectedItem = dt.Hour >= 12 ? "PM" : "AM";
        }

        var optionIndex = Array.FindIndex(AlertTypeOptions, o => o.Mode == t.Mode);
        AlertTypeBox.SelectedIndex = optionIndex >= 0 ? optionIndex : 0;

        SoundBox.SelectedItem = string.IsNullOrEmpty(t.SoundFileName) ? "(None)" : t.SoundFileName;
        PhraseBox.Text = t.Phrase ?? string.Empty;
        VolumeSlider.Value = t.Volume;
        VolumeValueText.Text = t.Volume.ToString();
        EnabledBox.IsChecked = t.Enabled;

        _recurrence = t.Recurrence;
        UpdateRecurrenceSummary();

        _isLoading = false;
        UpdateModeDependentUi();
    }

    private void UpdateRecurrenceSummary()
    {
        if (_recurrence is null)
        {
            RecurrenceSummaryText.Text = "Does not repeat";
            RecurrenceNotice.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecurrenceSummaryText.Text = RecurrenceCalculator.Describe(_recurrence);
            RecurrenceNotice.Visibility = Visibility.Visible;
        }
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        var anchor = _recurrence is not null ? _target.RecurrenceAnchor : BuildDateTime();
        var dialog = new RecurrenceWindow(anchor, _recurrence) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _recurrence = dialog.Result;
            UpdateRecurrenceSummary();
        }
    }

    private AlertMode CurrentMode =>
        AlertTypeOptions[Math.Max(AlertTypeBox.SelectedIndex, 0)].Mode;

    private void AlertTypeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        UpdateModeDependentUi();

        bool speaks = CurrentMode is AlertMode.SoundAndSpeech or AlertMode.SpeechOnly;
        if (_isNew && speaks && string.IsNullOrWhiteSpace(PhraseBox.Text))
            PhraseBox.Text = NameBox.Text;
    }

    private void UpdateModeDependentUi()
    {
        var mode = CurrentMode;

        bool speaks = mode is AlertMode.SoundAndSpeech or AlertMode.SpeechOnly;
        PhraseBox.IsEnabled = speaks;
        PhraseLabel.Opacity = speaks ? 1.0 : 0.5;

        bool playsSound = mode is AlertMode.SoundOnly or AlertMode.SoundAndSpeech;
        SoundBox.IsEnabled = playsSound;
        SoundLabel.Opacity = playsSound ? 1.0 : 0.5;
        VolumeSlider.IsEnabled = playsSound;
        VolumeLabel.Opacity = playsSound ? 1.0 : 0.5;
        VolumeValueText.Opacity = playsSound ? 1.0 : 0.5;
    }

    private void EnabledBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (DisabledHint is not null)
            DisabledHint.Visibility = EnabledBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeValueText is not null)
            VolumeValueText.Text = ((int)e.NewValue).ToString();
    }

    private DateTime BuildDateTime()
    {
        var date = DatePickerControl.SelectedDate ?? DateTime.Today;
        int minute = int.Parse((string)(MinuteBox.SelectedItem ?? "0"));
        int second = int.Parse((string)(SecondBox.SelectedItem ?? "0"));

        int hour24;
        if (_use24Hour)
        {
            hour24 = int.Parse((string)(HourBox.SelectedItem ?? "0"));
        }
        else
        {
            int hour = int.Parse((string)(HourBox.SelectedItem ?? "12"));
            bool isPm = (string)(AmPmBox.SelectedItem ?? "AM") == "PM";
            hour24 = hour % 12;
            if (isPm) hour24 += 12;
        }

        return new DateTime(date.Year, date.Month, date.Day, hour24, minute, second);
    }

    private (string? soundFile, AlertMode mode, string? phrase, int volume) BuildAlertSettings()
    {
        var soundSelection = SoundBox.SelectedItem as string;
        string? soundFile = soundSelection == "(None)" ? null : soundSelection;
        var phrase = PhraseBox.Text;
        return (soundFile, CurrentMode, phrase, (int)VolumeSlider.Value);
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var (soundFile, mode, phrase, volume) = BuildAlertSettings();
        var services = AppServices.Instance;
        var soundPath = services.ResolveSoundPath(soundFile);
        services.AlertPlayback.Start(soundPath, mode, phrase, repeatUntilDismissed: false, volume: volume / 100f);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Please enter a name for this timer.", "Pendulum", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppServices.Instance.AlertPlayback.Stop();

        var (soundFile, mode, phrase, volume) = BuildAlertSettings();

        var scheduledAt = BuildDateTime();
        var now = DateTime.Now;
        var enabledByUser = EnabledBox.IsChecked == true;

        _target.Name = NameBox.Text.Trim();
        _target.RecurrenceAnchor = scheduledAt;
        _target.Recurrence = _recurrence;
        _target.Mode = mode;
        _target.SoundFileName = soundFile;
        _target.Phrase = phrase;
        _target.Volume = volume;

        DateTime? firstOccurrence = scheduledAt > now
            ? scheduledAt
            : _recurrence is not null
                ? RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(_recurrence, scheduledAt, scheduledAt, now)
                : null;

        if (firstOccurrence is not null)
        {
            // A future start time, or a recurring schedule that still has a future occurrence.
            _target.TriggerAt = firstOccurrence.Value;
            _target.HasFired = false;
            _target.Enabled = enabledByUser;
        }
        else
        {
            // A one-shot reminder in the past, or a recurring schedule with nothing left to fire.
            _target.TriggerAt = scheduledAt;
            _target.HasFired = true;
            _target.Enabled = false;
        }

        Result = _target;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Instance.AlertPlayback.Stop();
        DialogResult = false;
    }
}
