namespace Pendulum.Core.Engine;

/// Which of the open alerts makes a sound: only the newest. The others stay on screen, silent and
/// waiting their turn. When the ringing one is dismissed or snoozed, the next newest rings, and
/// says what it is once. A pure decision, tested without audio or windows: the app applies each
/// <see cref="Handover"/> to the alerts' playback.
public sealed class AlertQueue<T> where T : class
{
    private readonly List<T> _open = new();

    /// What to change: the alert to silence, the one to ring, and whether it rings as a waiting
    /// alert whose turn has come (spoken once).
    public readonly record struct Handover(T? Silence, T? Ring, bool TurnCame);

    /// The alert making the sound now, or null when none is open.
    public T? Ringing { get; private set; }

    /// Every open alert, newest first.
    public IReadOnlyList<T> NewestFirst => _open.AsEnumerable().Reverse().ToList();

    /// The open alerts not ringing, newest first.
    public IReadOnlyList<T> Waiting => NewestFirst.Where(a => !ReferenceEquals(a, Ringing)).ToList();

    public int Count => _open.Count;

    /// A new alert: it rings, and the one ringing before falls silent.
    public Handover Open(T alert)
    {
        if (_open.Contains(alert))
            return default;

        _open.Add(alert);
        var previous = Ringing;
        Ringing = alert;
        return new Handover(previous, alert, TurnCame: false);
    }

    /// An alert dismissed, snoozed or resolved: if it was ringing, the next newest rings now.
    public Handover Close(T alert)
    {
        if (!_open.Remove(alert))
            return default;

        if (!ReferenceEquals(alert, Ringing))
            return default;

        Ringing = _open.Count > 0 ? _open[^1] : null;
        return new Handover(null, Ringing, TurnCame: Ringing is not null);
    }
}
