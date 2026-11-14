using CommunityToolkit.Mvvm.ComponentModel;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public sealed record EditorAlertType(AlertMode Mode, string Text);

/// Edits a draft without changing the live reminder. All fields are usable without a window.
public partial class EditorViewModel : ObservableObject
{
    public IReadOnlyList<EditorAlertType> AlertTypes { get; } =
    [
        new(AlertMode.SoundOnly, Strings.Edit_SoundOnly),
        new(AlertMode.SoundAndSpeech, Strings.Edit_SoundAndSpeech),
        new(AlertMode.SpeechOnly, Strings.Edit_SpeechOnly),
    ];

    public bool IsNew { get; }
    public bool Use24HourTime { get; }
    public DateTime ShownAt { get; }
    public IReadOnlyList<string> Hours { get; }
    public IReadOnlyList<string> Minutes { get; } = Enumerable.Range(0, 60).Select(n => n.ToString("00")).ToArray();
    public IReadOnlyList<string> AmPm { get; } = [Strings.Edit_Am, Strings.Edit_Pm];
    public IReadOnlyList<string> Sounds { get; }
    private bool _loading = true;
    private bool _colourChosen;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private DateTime? date;
    [ObservableProperty] private string? hour;
    [ObservableProperty] private string? minute;
    [ObservableProperty] private string? second;
    [ObservableProperty] private int amPmIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Speaks), nameof(PlaysSound), nameof(SpeechOpacity), nameof(SoundOpacity))]
    private AlertMode mode;
    [ObservableProperty] private int soundIndex;
    [ObservableProperty] private string phrase = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumePercent))]
    private double volume;
    [ObservableProperty] private bool enabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecurrenceSummary), nameof(Repeats))]
    private RecurrenceRule? recurrence;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColourName))]
    private string colour = string.Empty;

    public bool Speaks => Mode is AlertMode.SoundAndSpeech or AlertMode.SpeechOnly;
    public bool PlaysSound => Mode is AlertMode.SoundOnly or AlertMode.SoundAndSpeech;
    public double SpeechOpacity => Speaks ? 1 : 0.5;
    public double SoundOpacity => PlaysSound ? 1 : 0.5;
    public int VolumePercent => (int)Volume;
    public bool Repeats => Recurrence is not null;
    public string RecurrenceSummary => Recurrence is null ? Strings.Edit_DoesNotRepeat : RecurrenceCalculator.Describe(Recurrence);
    public string ColourName => ReminderColorNames.Display(Colour);

    public EditorViewModel(TriggerTimer target, bool isNew, bool use24HourTime, IEnumerable<string> sounds, string initialColour)
    {
        IsNew = isNew;
        Use24HourTime = use24HourTime;
        ShownAt = RecurrenceCalculator.ScheduledOccurrence(target);
        Hours = use24HourTime ? Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray()
            : Enumerable.Range(1, 12).Select(h => h.ToString()).ToArray();
        var soundOptions = new[] { Strings.Edit_NoSound }.Concat(sounds).ToArray();
        Sounds = soundOptions;
        Name = target.Name;
        Date = ShownAt.Date;
        Hour = use24HourTime ? ShownAt.Hour.ToString("00") : ((ShownAt.Hour + 11) % 12 + 1).ToString();
        Minute = ShownAt.Minute.ToString("00");
        Second = ShownAt.Second.ToString("00");
        AmPmIndex = ShownAt.Hour >= 12 ? 1 : 0;
        Mode = AlertTypes.Any(t => t.Mode == target.Mode) ? target.Mode : AlertMode.SoundOnly;
        SoundIndex = string.IsNullOrEmpty(target.SoundFileName) ? 0 : Array.IndexOf(soundOptions, target.SoundFileName);
        Phrase = target.Phrase ?? string.Empty;
        Volume = Math.Clamp(target.Volume, 0, 100);
        Enabled = target.Enabled;
        Recurrence = target.RepeatRule;
        Colour = initialColour;
        _loading = false;
    }

    partial void OnModeChanged(AlertMode value)
    {
        if (!_loading && IsNew && Speaks && string.IsNullOrWhiteSpace(Phrase)) Phrase = Name;
    }

    partial void OnColourChanged(string value)
    {
        if (!_loading) _colourChosen = true;
    }

    public void ChooseColour(string value)
    {
        _colourChosen = true;
        Colour = value;
    }

    public DateTime ScheduledAt(DateTime now)
    {
        var selectedDate = Date ?? now.Date;
        var h = int.TryParse(Hour, out var parsed) ? parsed : Use24HourTime ? 0 : 12;
        if (!Use24HourTime) h = h % 12 + (AmPmIndex == 1 ? 12 : 0);
        var m = int.TryParse(Minute, out var minutes) ? minutes : 0;
        var s = int.TryParse(Second, out var seconds) ? seconds : 0;
        var edited = new DateTime(selectedDate.Year, selectedDate.Month, selectedDate.Day, h, m, s);
        // The controls show whole seconds. Keeping their original values must not discard the
        // finer precision of a relative deadline and accidentally replace an unchanged snooze.
        var shownSecond = ShownAt.AddTicks(-(ShownAt.Ticks % TimeSpan.TicksPerSecond));
        return edited == shownSecond ? ShownAt : edited;
    }

    public TriggerEditDraft CreateDraft(DateTime now) => new()
    {
        Name = Name,
        ShownAt = ShownAt,
        ScheduledAt = ScheduledAt(now),
        Recurrence = Recurrence,
        Mode = Mode,
        SoundFileName = SoundIndex > 0 && SoundIndex < Sounds.Count ? Sounds[SoundIndex] : null,
        Phrase = Phrase,
        Volume = VolumePercent,
        Enabled = Enabled,
        Color = Colour,
        ColorChosen = _colourChosen,
    };
}
