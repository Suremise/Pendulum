using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
        var whisperModel = settings.WhisperModelFileName;
        var useWhisper = settings.SpeechToTextEngine == SpeechToTextEngine.Whisper && !string.IsNullOrEmpty(whisperModel);

        ErrorText.Visibility = Visibility.Collapsed;
        MicButton.IsEnabled = false;
        MicButton.Icon = new SymbolIcon { Symbol = SymbolRegular.MicPulse24 };
        MicButton.ToolTip = "Listening…";

        _listenCts = new CancellationTokenSource();
        try
        {
            string? text;
            if (useWhisper)
            {
                var modelPath = Path.Combine(AppPaths.WhisperModelsDirectory, whisperModel!);
                text = await _whisperSpeech.ListenOnceAsync(modelPath, () => MicButton.ToolTip = "Transcribing…", _listenCts.Token);
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
                ShowError("Didn't catch that — try again, or type it instead.");
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-listen — nothing left to update.
        }
        catch (Exception)
        {
            ShowError("Couldn't start speech recognition. Make sure a microphone is set up in Windows.");
        }
        finally
        {
            _listenCts?.Dispose();
            _listenCts = null;
            MicButton.IsEnabled = true;
            MicButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Mic24 };
            MicButton.ToolTip = "Speak a reminder";
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
            ShowError("Couldn't find a time — try \"in 20 min\", \"3pm\", or \"tomorrow 9am\".");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            ShowError("Please enter a name for the reminder, not just a time.");
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

        AppServices.Instance.AddTrigger(trigger);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
