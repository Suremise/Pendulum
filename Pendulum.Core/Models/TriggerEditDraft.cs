namespace Pendulum.Core.Models;

/// The editor's proposed values, separate from the live reminder until Save is accepted.
public sealed record TriggerEditDraft
{
    public required string Name { get; init; }
    public required DateTime ShownAt { get; init; }
    public required DateTime ScheduledAt { get; init; }
    public RecurrenceRule? Recurrence { get; init; }
    public AlertMode Mode { get; init; }
    public string? SoundFileName { get; init; }
    public string? Phrase { get; init; }
    public int Volume { get; init; } = 100;
    public bool Enabled { get; init; } = true;
    public required string Color { get; init; }
    public bool ColorChosen { get; init; }
}
