namespace Pendulum.App.Services;

/// Keeps shell tooltip updates behind a small boundary that can be checked without Explorer.
internal sealed class TrayTooltipUpdater(Action<string> setText, Action<Exception> logFailure)
{
    private string? _lastAttempt;

    public void Update(string text)
    {
        if (text == _lastAttempt)
            return;

        // Remember failed attempts too: a stalled Explorer must not produce the same failure
        // on every tick. A different text tries again; the icon library owns shell recreation.
        _lastAttempt = text;
        try { setText(text); }
        catch (Exception ex) { logFailure(ex); }
    }
}
