namespace Pendulum.Core.Tests;

/// The released versions whose Data files are in Fixtures/ (see its README), for the tests that
/// load each with today's code.
public static class ReleaseFixtures
{
    public static TheoryData<string> All => new() { "v1.0.0", "v1.1.0", "v1.2.0", "v1.2.1" };

    /// How many reminders each release's sample holds: 1.2.0's adds one on a relative day.
    public static int Reminders(string release) => release is "v1.0.0" or "v1.1.0" ? 6 : 7;
}
