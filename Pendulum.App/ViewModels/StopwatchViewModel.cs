using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pendulum.App.ViewModels;

public partial class StopwatchViewModel : ObservableObject
{
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string displayTime = "00:00:00.0";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string startPauseLabel = "Start";

    public ObservableCollection<string> Laps { get; } = new();

    public bool HasLaps => Laps.Count > 0;

    public StopwatchViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, __) => UpdateDisplay();
        Laps.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasLaps));
    }

    private void UpdateDisplay()
    {
        var t = _stopwatch.Elapsed;
        DisplayTime = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    [RelayCommand]
    private void StartPause()
    {
        if (IsRunning)
        {
            _stopwatch.Stop();
            _timer.Stop();
        }
        else
        {
            _stopwatch.Start();
            _timer.Start();
        }

        IsRunning = !IsRunning;
        StartPauseLabel = IsRunning ? "Pause" : "Start";
    }

    [RelayCommand]
    private void Reset()
    {
        _stopwatch.Reset();
        _timer.Stop();
        IsRunning = false;
        StartPauseLabel = "Start";
        Laps.Clear();
        UpdateDisplay();
    }

    [RelayCommand]
    private void Lap()
    {
        if (IsRunning)
            Laps.Insert(0, DisplayTime);
    }
}
