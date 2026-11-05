using HomeShowing.Ai.Application;
using HomeShowing.Ai.Infrastructure;
using HomeShowing.BuildingBlocks.Tenancy;
using NodaTime;
using NodaTime.Testing;
using NodaTime.Text;

namespace Scheduling.Tests;

public class AvailabilityParserTests
{
    private static readonly DateTimeZone Denver = DateTimeZoneProviders.Tzdb["America/Denver"];
    // Wednesday 2026-11-04 10:00 in Denver
    private static readonly Instant Now = InstantPattern.ExtendedIso.Parse("2026-11-04T17:00:00Z").Value;
    private static readonly AvailabilityParsingService Service = new(new RuleBasedAvailabilityParser(), new FakeClock(Now), new AiRateLimiter(1000), new TenantContext());

    private static async Task<ParseResponse> Parse(string text) =>
        (await Service.ParseAsync(new ParseRequest(text, "America/Denver"), default)).Value;

    [Fact]
    public async Task The_demo_phrase_is_parsed_into_two_windows()
    {
        var r = await Parse("weekday evenings after 6 except Thursday, Saturday morning");
        Assert.Equal("rule-based", r.Provider);
        Assert.Equal(2, r.Windows.Count);
        Assert.Equal(new[] { "Monday", "Tuesday", "Wednesday", "Friday" }, r.Windows[0].Days);
        Assert.Equal(("18:00", "21:00"), (r.Windows[0].Start, r.Windows[0].End));
        Assert.Equal(new[] { "Saturday" }, r.Windows[1].Days);
        Assert.Equal(("08:00", "12:00"), (r.Windows[1].Start, r.Windows[1].End));
        Assert.Equal("Weekdays except Thursday 6:00 PM–9:00 PM; Sat 8:00 AM–12:00 PM", r.Summary);
    }

    [Theory]
    [InlineData("weekends", "Saturday,Sunday", "09:00", "18:00")]
    [InlineData("tuesday afternoon", "Tuesday", "12:00", "17:00")]
    [InlineData("sat 9-12", "Saturday", "09:00", "12:00")]
    [InlineData("Sunday between 2 and 5", "Sunday", "14:00", "17:00")]
    [InlineData("weekdays before 11am", "Monday,Tuesday,Wednesday,Thursday,Friday", "08:00", "11:00")]
    [InlineData("friday evening after 7:30", "Friday", "19:30", "21:00")]
    public async Task Common_phrases(string text, string days, string start, string end)
    {
        var w = (await Parse(text)).Windows.Single();
        Assert.Equal(days.Split(','), w.Days);
        Assert.Equal((start, end), (w.Start, w.End));
    }

    [Fact]
    public async Task A_time_without_days_inherits_the_previous_clause()
    {
        var r = await Parse("saturday morning and afternoon");
        Assert.All(r.Windows, w => Assert.Equal(new[] { "Saturday" }, w.Days));
        Assert.Equal(new[] { "08:00", "12:00" }, r.Windows.Select(w => w.Start));
    }

    [Fact]
    public async Task Ranges_cover_the_next_two_weeks_and_skip_the_past()
    {
        var r = await Parse("weekday mornings");
        // Wednesday 10:00 now: today's range starts at 10:15 (rounded up), not 08:00.
        Assert.Equal("2026-11-04T10:15", r.Ranges[0].Start);
        Assert.Equal("2026-11-04T12:00", r.Ranges[0].End);
        Assert.Equal(10, r.Ranges.Count); // 10 weekdays in 14 days, today included
    }

    [Fact]
    public async Task Today_and_tomorrow_resolve_against_now()
    {
        var w = (await Parse("tomorrow evening")).Windows.Single();
        Assert.Equal(new[] { "Thursday" }, w.Days);
    }

    [Fact]
    public async Task Gibberish_returns_no_windows_and_a_helpful_warning()
    {
        var r = await Parse("whenever works lol");
        Assert.Empty(r.Windows);
        Assert.Contains(r.Warnings, w => w.Contains("couldn't find"));
    }

    [Fact]
    public void Model_output_is_validated_server_side()
    {
        var ok = WindowRules.Validate([
            new AvailabilityWindow(["saturday", "Funday"], "09:00", "12:00"),
            new AvailabilityWindow(["Sunday"], "14:00", "10:00"),
            new AvailabilityWindow(["Monday"], "9am", "noon"),
        ], out var problems);
        Assert.Single(ok);
        Assert.Equal(new[] { "Saturday" }, ok[0].Days);
        Assert.Equal(2, problems.Count);
    }
}
