using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class ReminderSubmissionTests
{
    [Fact]
    public void Quick_reminder_accepts_before_exit_and_declines_after_exit() => UiThread.Invoke(() =>
    {
        var writes = 0;
        var services = new AppServices((_, _) => writes++);
        try
        {
            var now = new DateTime(2026, 11, 14, 12, 0, 0);
            Assert.True(QuickAddWindow.TrySubmit("tea in 20 min", now, services, out var error));
            Assert.Null(error);
            var reminder = Assert.Single(services.Triggers);
            Assert.Equal("tea", reminder.Name);
            Assert.Equal(now.AddMinutes(20), reminder.TriggerAt);

            services.Shutdown();
            var saved = writes;
            var refreshes = services.Engine.TriggerSetCount;
            Assert.False(QuickAddWindow.TrySubmit("coffee in 10 min", now, services, out error));
            Assert.Null(error);
            Assert.False(services.TryAddTrigger(new TriggerTimer()));
            Assert.Same(reminder, Assert.Single(services.Triggers));
            Assert.Equal(saved, writes);
            Assert.Equal(refreshes, services.Engine.TriggerSetCount);
            Assert.Throws<ObjectDisposedException>(() => services.AddTrigger(new TriggerTimer()));
        }
        finally { services.Shutdown(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_editor_only_selects_and_saves_an_accepted_submission(bool exitWhileEditing) => UiThread.Invoke(() =>
    {
        var writes = 0;
        var services = new AppServices((_, _) => writes++);
        try
        {
            var result = new TriggerTimer { Name = "tea" };
            var dialogs = new FakeDialogs { EditResult = result };
            if (exitWhileEditing) dialogs.OnEdit = services.Shutdown;
            var vm = new TriggersViewModel(services.Reminders, services.Settings, dialogs);

            vm.Add(services);

            Assert.True(Assert.Single(dialogs.Edits).IsNew);
            if (exitWhileEditing)
            {
                Assert.Null(vm.SelectedTrigger);
                Assert.Empty(services.Triggers);
                Assert.Equal(0, services.Engine.TriggerSetCount);
            }
            else
            {
                Assert.Same(result, vm.SelectedTrigger);
                Assert.Same(result, Assert.Single(services.Triggers));
                services.FlushPendingSaves();
                Assert.Equal(1, writes);
            }

            services.Shutdown();
            var saved = writes;
            vm.Add(services);
            Assert.Single(dialogs.Edits); // No editor is opened after shutdown starts.
            Assert.Equal(saved, writes);
        }
        finally { services.Shutdown(); }
    });
}
