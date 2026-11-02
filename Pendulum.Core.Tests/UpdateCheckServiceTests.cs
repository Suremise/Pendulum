using System.Net;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.Core.Tests;

/// The update check through a fake HTTP handler: no test reaches the network.
public class UpdateCheckServiceTests
{
    private static readonly Version Running = new(1, 1, 0);
    private static readonly DateTime Now = new(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<Uri> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public static FakeHandler Replying(HttpStatusCode status, string body) =>
            new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_respond(request));
        }
    }

    private static Task<string?> Check(AppSettings settings, FakeHandler handler, DateTime? now = null) =>
        UpdateCheckService.CheckForNewerVersionAsync(settings, Running, new HttpClient(handler), now ?? Now, CancellationToken.None);

    [Theory]
    [InlineData("1.2.0", true)]
    [InlineData("1.1.1", true)]
    [InlineData("2.0.0", true)]
    [InlineData("1.10.0", true)]
    [InlineData("1.2", true)]
    [InlineData("1.1.0", false)]
    [InlineData("1.0.9", false)]
    [InlineData("1.1", false)]
    [InlineData("0.9.0", false)]
    [InlineData("1.2.0-beta", false)]
    [InlineData("v1.2.0", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_higher_plain_version_is_newer(string? candidate, bool newer)
    {
        Assert.Equal(newer, UpdateCheckService.IsNewer(candidate, Running));
    }

    [Fact]
    public void Compares_by_number_not_by_text()
    {
        Assert.True(UpdateCheckService.IsNewer("1.10.0", new Version(1, 9, 0)));
        Assert.False(UpdateCheckService.IsNewer("1.9.0", new Version(1, 10, 0)));
    }

    // ---- Help > Check for updates…: asked now, whatever the cache ----

    private static Task<UpdateCheckResult> CheckNow(AppSettings settings, FakeHandler handler) =>
        UpdateCheckService.CheckNowAsync(settings, Running, new HttpClient(handler), Now, CancellationToken.None);

    [Fact]
    public async Task Checking_now_asks_GitHub_even_within_a_day_and_names_a_newer_version()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v1.3.0" }""");
        var settings = new AppSettings { LastUpdateCheckUtc = Now.AddHours(-1), LastKnownLatestVersion = "1.1.0" };

        var result = await CheckNow(settings, handler);

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.Newer, "1.3.0"), result);
        Assert.Single(handler.Requests);
        Assert.Equal("1.3.0", settings.LastKnownLatestVersion);
        Assert.Equal(Now, settings.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Checking_now_with_the_latest_running_says_so()
    {
        var result = await CheckNow(new AppSettings(), FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v1.1.0" }"""));

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, "1.1.0"), result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{ "tag_name": "v9.0.0" }""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{ "name": "no tag" }""")]
    public async Task Checking_now_when_GitHub_doesnt_answer_says_so_and_keeps_the_last_answer(HttpStatusCode status, string body)
    {
        var settings = new AppSettings { LastUpdateCheckUtc = Now.AddDays(-3), LastKnownLatestVersion = "1.1.0" };

        var result = await CheckNow(settings, FakeHandler.Replying(status, body));

        Assert.Equal(UpdateCheckOutcome.Unreachable, result.Outcome);
        Assert.Equal(Now.AddDays(-3), settings.LastUpdateCheckUtc);
        Assert.Equal("1.1.0", settings.LastKnownLatestVersion);
    }

    [Fact]
    public async Task A_newer_release_is_reported_and_remembered()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v1.2.0", "name": "Pendulum 1.2.0" }""");
        var settings = new AppSettings();

        var newer = await Check(settings, handler);

        Assert.Equal("1.2.0", newer);
        Assert.Equal("1.2.0", settings.LastKnownLatestVersion);
        Assert.Equal(Now, settings.LastUpdateCheckUtc);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/Suremise/Pendulum/releases/latest", request.ToString());
    }

    [Fact]
    public async Task The_same_release_as_running_reports_nothing()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v1.1.0" }""");
        var settings = new AppSettings();

        Assert.Null(await Check(settings, handler));
        Assert.Equal("1.1.0", settings.LastKnownLatestVersion);
    }

    [Fact]
    public async Task Within_a_day_of_the_last_check_it_answers_from_the_cache_without_asking()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v9.9.9" }""");
        var settings = new AppSettings { LastUpdateCheckUtc = Now.AddHours(-23), LastKnownLatestVersion = "1.2.0" };

        var newer = await Check(settings, handler);

        Assert.Equal("1.2.0", newer);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task After_a_day_it_asks_again()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "v1.3.0" }""");
        var settings = new AppSettings { LastUpdateCheckUtc = Now.AddHours(-25), LastKnownLatestVersion = "1.2.0" };

        Assert.Equal("1.3.0", await Check(settings, handler));
        Assert.Single(handler.Requests);
        Assert.Equal(Now, settings.LastUpdateCheckUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{ "message": "Not Found" }""")]
    [InlineData(HttpStatusCode.Forbidden, """{ "message": "rate limited" }""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{ "name": "no tag" }""")]
    [InlineData(HttpStatusCode.OK, """{ "tag_name": null }""")]
    public async Task A_failed_check_changes_nothing_so_the_next_launch_tries_again(HttpStatusCode status, string body)
    {
        var handler = FakeHandler.Replying(status, body);
        var lastCheck = Now.AddDays(-2);
        var settings = new AppSettings { LastUpdateCheckUtc = lastCheck, LastKnownLatestVersion = "1.2.0" };

        var newer = await Check(settings, handler);

        Assert.Equal("1.2.0", newer);
        Assert.Equal(lastCheck, settings.LastUpdateCheckUtc);
        Assert.Equal("1.2.0", settings.LastKnownLatestVersion);
    }

    [Fact]
    public async Task Being_offline_is_silent()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var settings = new AppSettings();

        Assert.Null(await Check(settings, handler));
        Assert.Null(settings.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task An_odd_tag_is_remembered_but_never_reported_as_newer()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{ "tag_name": "nightly-2026-06-03" }""");
        var settings = new AppSettings();

        Assert.Null(await Check(settings, handler));
    }
}
