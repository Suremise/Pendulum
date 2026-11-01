using System.ComponentModel;

namespace Pendulum.Core.Models;

/// The Reminders list's filters, kept in the settings' ReminderFilter* values: each change is
/// saved as a setting, so the list opens as it was left. Changed fires once for each change that
/// can alter which reminders show (not for showing or hiding the filter row); PropertyChanged
/// names the property that changed, whether through here or in the settings themselves.
public sealed class ReminderFilter : INotifyPropertyChanged
{
    private const string SettingsPrefix = "ReminderFilter"; // not user-visible: settings' property names

    private readonly AppSettings _settings;

    public ReminderFilter(AppSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// A change that can alter which reminders pass.
    public event EventHandler? Changed;

    public bool RowVisible { get => _settings.ReminderFilterRowVisible; set => _settings.ReminderFilterRowVisible = value; }
    public string Name { get => _settings.ReminderFilterName; set => _settings.ReminderFilterName = value ?? string.Empty; }

    public bool TypeFixed { get => _settings.ReminderFilterTypeFixed; set => _settings.ReminderFilterTypeFixed = value; }
    public bool TypeScheduled { get => _settings.ReminderFilterTypeScheduled; set => _settings.ReminderFilterTypeScheduled = value; }

    public bool StatusUpcoming { get => _settings.ReminderFilterStatusUpcoming; set => _settings.ReminderFilterStatusUpcoming = value; }
    public bool StatusSpent { get => _settings.ReminderFilterStatusSpent; set => _settings.ReminderFilterStatusSpent = value; }
    public bool StatusDisabled { get => _settings.ReminderFilterStatusDisabled; set => _settings.ReminderFilterStatusDisabled = value; }

    public bool ModeSoundOnly { get => _settings.ReminderFilterModeSoundOnly; set => _settings.ReminderFilterModeSoundOnly = value; }
    public bool ModeSoundAndSpeech { get => _settings.ReminderFilterModeSoundAndSpeech; set => _settings.ReminderFilterModeSoundAndSpeech = value; }
    public bool ModeSpeechOnly { get => _settings.ReminderFilterModeSpeechOnly; set => _settings.ReminderFilterModeSpeechOnly = value; }

    public DateTime? From { get => _settings.ReminderFilterFrom; set => _settings.ReminderFilterFrom = value; }
    public DateTime? To { get => _settings.ReminderFilterTo; set => _settings.ReminderFilterTo = value; }

    private bool HasType => TypeFixed || TypeScheduled;
    private bool HasStatus => StatusUpcoming || StatusSpent || StatusDisabled;
    private bool HasMode => ModeSoundOnly || ModeSoundAndSpeech || ModeSpeechOnly;

    /// Whether any filter narrows the list, shown or not.
    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Name) || HasType || HasStatus || HasMode || From is not null || To is not null;

    /// Clears the date range.
    public void ClearDates()
    {
        From = null;
        To = null;
    }

    /// Whether a reminder shows in the filtered list. Within a group (type, status, mode) any
    /// ticked box lets a reminder through; across groups, every group must.
    public bool Matches(TriggerTimer t)
    {
        if (!string.IsNullOrWhiteSpace(Name) && t.Name.IndexOf(Name, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (HasType && !((TypeScheduled && t.Recurrence is not null) || (TypeFixed && t.Recurrence is null)))
            return false;

        if (HasStatus)
        {
            var isDisabled = !t.Enabled && !t.HasFired;
            var isUpcoming = t.Enabled && !t.HasFired;

            if (!((StatusUpcoming && isUpcoming) || (StatusSpent && t.HasFired) || (StatusDisabled && isDisabled)))
                return false;
        }

        if (HasMode && !((ModeSoundOnly && t.Mode == AlertMode.SoundOnly)
                         || (ModeSoundAndSpeech && t.Mode == AlertMode.SoundAndSpeech)
                         || (ModeSpeechOnly && t.Mode == AlertMode.SpeechOnly)))
            return false;

        if (From is not null && t.TriggerAt.Date < From.Value.Date)
            return false;

        if (To is not null && t.TriggerAt.Date > To.Value.Date)
            return false;

        return true;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || !name.StartsWith(SettingsPrefix, StringComparison.Ordinal))
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name[SettingsPrefix.Length..]));
        if (name != nameof(AppSettings.ReminderFilterRowVisible))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
