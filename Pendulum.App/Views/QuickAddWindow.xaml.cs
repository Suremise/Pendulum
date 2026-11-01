using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Parsing;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class QuickAddWindow : FluentWindow
{
    private readonly SpeechRecognitionService _windowsSpeech = new();
    private readonly WhisperRecognitionService _whisperSpeech = new();
    private CancellationTokenSource? _listenCts;

    public QuickAddWindow()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // Summoned by a global hotkey, so some other app usually owns focus — force
            // this window to the foreground so keystrokes land in the input box immediately.
            Win32Interop.ForceForegroundWindow(new WindowInteropHelper(this).Handle);
            Keyboard.Focus(InputBox);

            if (AppServices.Instance.Settings.QuickAddAutoListen)
                _ = StartListeningAsync();
        };
        Closed += (_, __) =>
        {
            _listenCts?.Cancel();
            _windowsSpeech.Dispose();
            _whisperSpeech.Dispose();
        };
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e) => await StartListeningAsync();

    private async Task StartListeningAsync()
    {
        if (_listenCts is not null)
            return;

        var settings = AppServices.Instance.Settings;
        // Only a plain file name in Data\WhisperModels\ is ever loaded, and only if it's there.
        // And Whisper needs Microsoft's Visual C++ runtime: said before listening, not after.
        var choice = DictationChoice.For(settings.SpeechToTextEngine, settings.WhisperModelFileName, AppPaths.WhisperModelsDirectory,
            () => WhisperRuntime.MissingNativeDependencies());
        if (choice.Problem is { } problem)
        {
            ShowError(MessageFor(problem));
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        MicButton.IsEnabled = false;
        MicButton.Icon = new SymbolIcon { Symbol = SymbolRegular.MicPulse24 };
        MicButton.ToolTip = Strings.QuickAdd_Listening;

        _listenCts = new CancellationTokenSource();
        try
        {
            string? text;
            if (choice.UseWhisper)
            {
                text = await _whisperSpeech.ListenOnceAsync(choice.WhisperModelPath!, () => MicButton.ToolTip = Strings.QuickAdd_Transcribing, _listenCts.Token);
            }
            else
            {
                text = await _windowsSpeech.ListenOnceAsync(_listenCts.Token);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                InputBox.Text = text;
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            else
            {
                ShowError(Strings.QuickAdd_DidntCatch);
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-listen — nothing left to update.
        }
        catch (Exception ex)
        {
            CrashLog.Write("QuickAdd listen", ex);  // not user-visible
            ShowError(MessageFor(DictationChoice.Classify(ex)));
        }
        finally
        {
            _listenCts?.Dispose();
            _listenCts = null;
            MicButton.IsEnabled = true;
            MicButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Mic24 };
            MicButton.ToolTip = Strings.QuickAdd_SpeakAReminder;
            Keyboard.Focus(InputBox);
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAdd();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => TryAdd();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TryAdd()
    {
        var result = QuickAddParser.Parse(InputBox.Text, DateTime.Now);

        if (result.When is null)
        {
            ShowError(result.Refusal == QuickAddRefusal.TimePassedToday ? Strings.QuickAdd_TimePassedToday : Strings.QuickAdd_NoTime);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            ShowError(Strings.QuickAdd_NoName);
            return;
        }

        var trigger = new TriggerTimer
        {
            Name = result.Name,
            TriggerAt = result.When.Value,
            RecurrenceAnchor = result.When.Value,
            SoundFileName = AppServices.Instance.Settings.DefaultSoundFileName,
            Mode = AlertMode.SoundOnly
        };
        // A span from now ("in 20 min") keeps its real instant, so it's right across a clock change.
        trigger.SetDue(result.When.Value, result.DueUtc);

        AppServices.Instance.AddTrigger(trigger);
        DialogResult = true;
    }

    /// What Quick Reminder says when speaking a reminder didn't work.
    internal static string MessageFor(DictationProblem problem) => problem switch
    {
        DictationProblem.Microphone => Strings.QuickAdd_MicFailed,
        DictationProblem.WhisperModelMissing => Strings.QuickAdd_WhisperModelMissing,
        DictationProblem.WhisperModelUnreadable => Strings.QuickAdd_WhisperModelUnreadable,
        DictationProblem.RecognizerMissing => Strings.QuickAdd_RecognizerMissing,
        DictationProblem.WhisperRuntimeMissing => Strings.QuickAdd_WhisperRuntimeMissing,
        _ => Strings.QuickAdd_ListenFailed,
    };

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
