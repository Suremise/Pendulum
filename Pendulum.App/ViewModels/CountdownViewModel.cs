using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pendulum.App.Services;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public partial class CountdownViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private DateTime _targetTime;
    private TimeSpan _remainingWhenPaused;
    private TimeSpan _committedDuration;

    [ObservableProperty] private int days;
    [ObservableProperty] private int hours;
    [ObservableProperty] private int minutes = 5;
    [ObservableProperty] private int seconds;

    [ObservableProperty] private string label = "Countdown";
    [ObservableProperty] private bool speakPhrase;
    [ObservableProperty] private string? soundFileName;
    [ObservableProperty] private string phrase = "Time's up";

    [ObservableProperty] private string displayRemaining = "00:00:00:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDurationCommand))]
    private bool isRunning;

    [ObservableProperty] private bool isPausedState;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool hasSetDuration;

    [ObservableProperty] private List<string> soundOptions = new();

    public CountdownViewModel()
    {
        SoundFileName = AppServices.Instance.Settings.DefaultSoundFileName;
        SoundOptions = AppServices.Instance.GetAvailableSoundFiles();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, __) => Tick();

        DisplayRemaining = FormatSpan(InputDuration);
    }

    private TimeSpan InputDuration => new(Days, Hours, Minutes, Seconds);

    private AlertMode Mode => SpeakPhrase ? AlertMode.SoundAndSpeech : AlertMode.SoundOnly;

    private bool CanSetDuration() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanSetDuration))]
    private void SetDuration()
    {
        _committedDuration = InputDuration;
        HasSetDuration = _committedDuration > TimeSpan.Zero;
        DisplayRemaining = FormatSpan(_committedDuration);
    }

    private bool CanStart() => HasSetDuration && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (!CanStart())
            return;

        _targetTime = DateTime.Now + _committedDuration;
        IsRunning = true;
        IsPausedState = false;
        _timer.Start();
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsRunning)
            return;

        _remainingWhenPaused = _targetTime - DateTime.Now;
        if (_remainingWhenPaused < TimeSpan.Zero)
            _remainingWhenPaused = TimeSpan.Zero;

        _timer.Stop();
        IsPausedState = true;
    }

    [RelayCommand]
    private void Resume()
    {
        if (!IsPausedState)
            return;

        _targetTime = DateTime.Now + _remainingWhenPaused;
        IsPausedState = false;
        _timer.Start();
    }

    [RelayCommand]
    private void Reset()
    {
        _timer.Stop();
        IsRunning = false;
        IsPausedState = false;
        HasSetDuration = false;
        _committedDuration = TimeSpan.Zero;
        DisplayRemaining = FormatSpan(InputDuration);
    }

    private void Tick()
    {
        var remaining = _targetTime - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            DisplayRemaining = FormatSpan(TimeSpan.Zero);
            _timer.Stop();
            IsRunning = false;
            Fire();
            return;
        }

        DisplayRemaining = FormatSpan(remaining);
    }

    private void Fire()
    {
        AlertCoordinator.Fire(
            string.IsNullOrWhiteSpace(Label) ? "Countdown" : Label,
            "Countdown finished",
            SoundFileName,
            Mode,
            Phrase,
            canSnooze: false,
            onSnooze: null,
            onDismiss: Reset,
            volumePercent: 100);
    }

    private static string FormatSpan(TimeSpan t) =>
        $"{(int)t.TotalDays:00}:{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
}
