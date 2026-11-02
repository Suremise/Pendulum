namespace Pendulum.Core.Models;

/// Each reminder's colour, one of the palette's eight (assets/README.md, "The palette"), saved by
/// its name in the reminder's optional <see cref="TriggerTimer.Color"/>: the theme picks the shade
/// for light and dark, so a name never needs rewriting.
///
/// - A new reminder takes the colour fewest reminders have (<see cref="ForNew"/>), so reminders
///   made one after another are told apart.
/// - A saved reminder without one (every reminder from before 1.2.0) gets a colour worked out
///   from its Id as it loads (<see cref="Stable"/>): the same one every time and on every PC, so
///   the calendar never reshuffles. It's written with the next save, never on its own.
/// - A name this version doesn't know (from a later one) is kept as it is, and shown as the
///   worked-out colour.
public static class ReminderColors
{
    /// The palette's names, in hue order from rose round the colour wheel, as saved.
    public static readonly IReadOnlyList<string> All =
        ["terracotta", "ochre", "olive", "sage", "teal", "blue", "violet", "plum"]; // not user-visible: saved names

    /// The order new reminders take them in, the least used first, ties in this order: each next
    /// to colours far from it on the wheel.
    public static readonly IReadOnlyList<string> AssignOrder =
        ["teal", "terracotta", "blue", "ochre", "violet", "sage", "plum", "olive"]; // not user-visible

    public static bool IsKnown(string? name) => name is not null && All.Contains(name);

    /// The colour a reminder shows: its own, or for none (or one this version doesn't know), the
    /// one worked out from its Id.
    public static string Effective(TriggerTimer reminder) =>
        IsKnown(reminder.Color) ? reminder.Color! : Stable(reminder.Id);

    /// A colour from an Id, the same every time.
    public static string Stable(Guid id)
    {
        var bytes = id.ToByteArray();
        var hash = 0u;
        foreach (var b in bytes)
            hash = hash * 31 + b;
        return AssignOrder[(int)(hash % (uint)AssignOrder.Count)];
    }

    /// The colour for a new reminder: the one fewest of these have, ties in
    /// <see cref="AssignOrder"/>.
    public static string ForNew(IEnumerable<TriggerTimer> existing)
    {
        var counts = AssignOrder.ToDictionary(c => c, _ => 0);
        foreach (var reminder in existing)
            counts[Effective(reminder)]++;
        return AssignOrder.OrderBy(c => counts[c]).ThenBy(c => AssignOrder.IndexOf(c)).First();
    }

    private static int IndexOf(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }
}
