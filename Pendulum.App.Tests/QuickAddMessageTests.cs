using Pendulum.App.Resources;
using Pendulum.App.Views;
using Pendulum.Core.Speech;

namespace Pendulum.App.Tests;

/// What Quick Reminder says when speaking a reminder fails: each reason its own words, and only
/// a microphone problem blamed on the microphone. No window is built.
public sealed class QuickAddMessageTests
{
    [Fact]
    public void Each_problem_has_its_own_message()
    {
        var messages = Enum.GetValues<DictationProblem>().Select(QuickAddWindow.MessageFor).ToList();
        Assert.Equal(messages.Count, messages.Distinct().Count());
        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
    }

    [Fact]
    public void Only_a_microphone_problem_is_blamed_on_the_microphone()
    {
        Assert.Equal(Strings.QuickAdd_MicFailed, QuickAddWindow.MessageFor(DictationProblem.Microphone));
        foreach (var problem in Enum.GetValues<DictationProblem>().Where(p => p != DictationProblem.Microphone))
            Assert.NotEqual(Strings.QuickAdd_MicFailed, QuickAddWindow.MessageFor(problem));
    }

    [Fact]
    public void A_missing_or_damaged_model_points_to_Settings()
    {
        Assert.Contains(Strings.Settings_QuickReminder, QuickAddWindow.MessageFor(DictationProblem.WhisperModelMissing), StringComparison.Ordinal);
        Assert.Contains(Strings.Settings_QuickReminder, QuickAddWindow.MessageFor(DictationProblem.WhisperModelUnreadable), StringComparison.Ordinal);
    }
}
