using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Pendulum.Core.Models;

namespace Pendulum.Core.Services;

/// What Help > Check for updates… found: a newer version (named), the running one is the
/// latest, or GitHub couldn't be reached.
public enum UpdateCheckOutcome
{
    Newer,
    UpToDate,
    Unreachable,
}

public readonly record struct UpdateCheckResult(UpdateCheckOutcome Outcome, string? Version);

/// Checks GitHub Releases for a version newer than the one currently running: Pendulum's only
/// request of its own. At startup, only when "Check for updates on startup" is on: a single GET
/// request, cached for a day, that fails silently so it can never block startup or surprise
/// someone offline. And when the user chooses Help > Check for updates…: the same request, then.
public static class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Suremise/Pendulum/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    static UpdateCheckService()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pendulum", "1"));
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// Returns the newer version's number (e.g. "1.2.0") if one is known, or null if the
    /// running version is already current. Only reaches out to GitHub if the cached result
    /// is more than a day old; a failed check (offline, GitHub unreachable, unexpected
    /// response) leaves the cache untouched so the very next launch tries again, rather than
    /// throttling retries for a full day off the back of one failed attempt.
    public static Task<string?> CheckForNewerVersionAsync(AppSettings settings, Version currentVersion, CancellationToken cancellationToken) =>
        CheckForNewerVersionAsync(settings, currentVersion, Http, DateTime.UtcNow, cancellationToken);

    /// For tests: the same check, through the given client (a fake handler, so no network) at the given time.
    internal static async Task<string?> CheckForNewerVersionAsync(
        AppSettings settings, Version currentVersion, HttpClient http, DateTime utcNow, CancellationToken cancellationToken)
    {
        var dueForRecheck = settings.LastUpdateCheckUtc is null
            || utcNow - settings.LastUpdateCheckUtc.Value > CheckInterval;

        if (dueForRecheck)
            await RefreshLatestVersionAsync(settings, http, utcNow, cancellationToken);

        return IsNewer(settings.LastKnownLatestVersion, currentVersion) ? settings.LastKnownLatestVersion : null;
    }

    /// Help > Check for updates…: asks GitHub now, whatever the startup setting and however
    /// recent the last answer, and says what it found. A successful answer is kept, as the
    /// startup check keeps it.
    public static Task<UpdateCheckResult> CheckNowAsync(AppSettings settings, Version currentVersion, CancellationToken cancellationToken) =>
        CheckNowAsync(settings, currentVersion, Http, DateTime.UtcNow, cancellationToken);

    /// For tests: the same, through the given client at the given time.
    internal static async Task<UpdateCheckResult> CheckNowAsync(
        AppSettings settings, Version currentVersion, HttpClient http, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (!await RefreshLatestVersionAsync(settings, http, utcNow, cancellationToken))
            return new UpdateCheckResult(UpdateCheckOutcome.Unreachable, null);

        var latest = settings.LastKnownLatestVersion;
        return IsNewer(latest, currentVersion)
            ? new UpdateCheckResult(UpdateCheckOutcome.Newer, latest)
            : new UpdateCheckResult(UpdateCheckOutcome.UpToDate, latest);
    }

    /// Asks GitHub for the latest release and keeps its version: true when it answered.
    private static async Task<bool> RefreshLatestVersionAsync(AppSettings settings, HttpClient http, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagProp) && tagProp.GetString() is { } tag)
            {
                settings.LastKnownLatestVersion = tag.TrimStart('v', 'V');
                settings.LastUpdateCheckUtc = utcNow;
                return true;
            }
            return false;
        }
        catch
        {
            // Offline, GitHub unreachable, unexpected response shape — this is a background
            // nicety, never something to surface as an error.
            return false;
        }
    }

    internal static bool IsNewer(string? candidate, Version currentVersion) =>
        Version.TryParse(candidate, out var parsed) && parsed > currentVersion;
}
