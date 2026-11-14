using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class TriggerEditWindow : FluentWindow
{
    private readonly TriggerTimer _target;
    private readonly AlertPlaybackController _previewPlayback;
    public EditorViewModel Editor { get; }
    public TriggerTimer? Result { get; private set; }

    public TriggerEditWindow(TriggerTimer target, bool isNew)
    {
        InitializeComponent();
        _target = target;
        var services = AppServices.Instance;
        var colour = isNew && target.Color is null ? ReminderColors.ForNew(services.Triggers) : ReminderColors.Effective(target);
        Editor = new EditorViewModel(target, isNew, services.Settings.Use24HourTime, services.GetAvailableSoundFiles(), colour);
        DataContext = Editor;
        Title = isNew ? Strings.Edit_NewReminder : Strings.Edit_EditReminder;
        EditorTitleBar.Title = Title;
        AmPmSpacerColumn.Width = new GridLength(Editor.Use24HourTime ? 0 : 8);
        AmPmColumn.Width = new GridLength(Editor.Use24HourTime ? 0 : 76);
        BuildColourChoices();

        _previewPlayback = services.CreatePlayback(problem => Dispatcher.BeginInvoke(() => ShowTestProblem(problem)));
        Editor.PropertyChanged += OnEditorChanged;
        Closed += (_, _) =>
        {
            Editor.PropertyChanged -= OnEditorChanged;
            _previewPlayback.Dispose();
        };
    }

    private void BuildColourChoices()
    {
        foreach (var name in ReminderColors.All)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColourSwatch"), // not user-visible: a resource key
                GroupName = nameof(ColourChoices),
                Tag = name,
                ToolTip = ReminderColorNames.Display(name),
                IsChecked = name == Editor.Colour,
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, ReminderColorNames.Display(name));
            swatch.Checked += (_, _) => Editor.ChooseColour(name);
            ColourChoices.Children.Add(swatch);
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Editor.Colour))
            foreach (var swatch in ColourChoices.Children.OfType<RadioButton>())
                swatch.IsChecked = (string)swatch.Tag == Editor.Colour;
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        var anchor = Editor.Repeats ? _target.RecurrenceAnchor : Editor.ScheduledAt(DateTime.Now);
        var dialog = new RecurrenceWindow(anchor, Editor.Recurrence) { Owner = this };
        if (dialog.ShowDialog() == true) Editor.Recurrence = dialog.Result;
    }

    /// Test tells the user what did not play, without changing the live reminder.
    private void ShowTestProblem(AlertProblem problem)
    {
        var message = problem switch
        {
            { Step: AlertStep.Sound } => Strings.Edit_TestSoundFailed,
            { FellBackToWindowsSpeech: true } => Strings.Edit_TestPiperFellBack,
            _ => Strings.Edit_TestSpeechFailed,
        };
        System.Windows.MessageBox.Show(this, string.Format(message, problem.Error.Message), Strings.App_Name, System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = Editor.CreateDraft(DateTime.Now);
        _previewPlayback.Start(AppServices.Instance.ResolveSoundPath(draft.SoundFileName), draft.Mode, draft.Phrase,
            repeatUntilDismissed: false, volume: draft.Volume / 100f);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Editor.Name))
        {
            System.Windows.MessageBox.Show(this, Strings.Edit_NameRequired, Strings.App_Name, System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var now = DateTime.Now;
        var draft = Editor.CreateDraft(now);
        var (_, next) = TriggerReconciler.ScheduleAfterEdit(_target, draft, now);
        if (next is null && draft.Enabled)
        {
            var choice = System.Windows.MessageBox.Show(this,
                string.Format(Strings.Edit_PastDate, DisplayFormat.DateAndTime(draft.ScheduledAt)), Strings.App_Name,
                System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (choice == System.Windows.MessageBoxResult.No) return;
        }

        _previewPlayback.Stop();
        void Apply() => TriggerReconciler.ApplyEdit(_target, draft, now);
        if (Editor.IsNew) Apply();
        else if (!AppServices.Instance.Update(_target, Apply))
        {
            DialogResult = false;
            return;
        }
        Result = _target;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _previewPlayback.Stop();
        DialogResult = false;
    }
}
