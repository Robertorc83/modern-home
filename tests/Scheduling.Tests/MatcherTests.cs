using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Domain;
using NodaTime;
using NodaTime.Text;

namespace Scheduling.Tests;

public class MatcherTests
{
    private static readonly DateTimeZone Denver = DateTimeZoneProviders.Tzdb["America/Denver"];
    private static readonly DateTimeZone Toronto = DateTimeZoneProviders.Tzdb["America/Toronto"];
    private static readonly Instant Now = InstantPattern.ExtendedIso.Parse("2026-11-02T15:00:00Z").Value;

    private static LocalDateTime L(string s) => LocalDateTimePattern.ExtendedIso.Parse(s).Value;

    private static ShowingRules SaturdayMorning(int slot = 30) => new()
    {
        SlotMinutes = slot, HorizonDays = 7, LeadTimeHours = 0,
        Weekly = [new WeeklyWindow { DayOfWeek = "Saturday", Start = "09:00", End = "12:00" }],
    };

    [Fact]
    public void Toronto_buyer_matches_a_denver_listing_in_their_own_time()
    {
        // Denver 09:00-12:00 MST = Toronto 11:00-14:00 EST. Buyer is free 12:00-13:00 Toronto time = 10:00-11:00 Denver.
        var open = SlotGenerator.Generate(SaturdayMorning(), Denver, Now);
        var ranges = Matcher.ToInstants([new LocalRange(L("2026-11-07T12:00:00"), L("2026-11-07T13:00:00"))], Toronto);

        var matched = Matcher.Within(open, ranges);

        Assert.Equal(2, matched.Count);
        Assert.Equal(new LocalTime(10, 0), matched[0].Start.InZone(Denver).TimeOfDay);
        Assert.Equal(new LocalTime(12, 0), matched[0].Start.InZone(Toronto).TimeOfDay);
    }

    [Fact]
    public void A_slot_must_fit_entirely_inside_the_range()
    {
        var open = SlotGenerator.Generate(SaturdayMorning(60), Denver, Now);
        var ranges = Matcher.ToInstants([new LocalRange(L("2026-11-07T09:30:00"), L("2026-11-07T11:00:00"))], Denver);
        Assert.Equal(new[] { new LocalTime(10, 0) }, Matcher.Within(open, ranges).Select(s => s.Start.InZone(Denver).TimeOfDay));
    }

    [Fact]
    public void Listings_are_ranked_by_number_of_matching_slots_and_empty_ones_dropped()
    {
        var a = SlotGenerator.Generate(SaturdayMorning(60), Denver, Now);  // 3 slots
        var b = SlotGenerator.Generate(SaturdayMorning(30), Denver, Now);  // 6 slots
        var none = SlotGenerator.Generate(new ShowingRules { HorizonDays = 7, LeadTimeHours = 0, Weekly = [new() { DayOfWeek = "Sunday", Start = "09:00", End = "10:00" }] }, Denver, Now);
        var ranges = Matcher.ToInstants([new LocalRange(L("2026-11-07T08:00:00"), L("2026-11-07T13:00:00"))], Denver);

        var ranked = Matcher.Rank(new[] { ("A", a), ("B", b), ("C", none) }, ranges);

        Assert.Equal(new[] { "B", "A" }, ranked.Select(r => r.Listing));
        Assert.Equal(6, ranked[0].Slots.Count);
    }

    [Fact]
    public void Slots_are_grouped_by_day_in_the_viewer_zone()
    {
        // A 10:30 p.m. Denver slot is 12:30 a.m. the next day in Toronto.
        var rules = new ShowingRules { SlotMinutes = 30, HorizonDays = 7, LeadTimeHours = 0, Weekly = [new() { DayOfWeek = "Friday", Start = "21:30", End = "23:00" }] };
        var open = SlotGenerator.Generate(rules, Denver, Now);
        var days = Matcher.GroupByDay(open, Toronto);
        Assert.Equal(new[] { new LocalDate(2026, 11, 6), new LocalDate(2026, 11, 7) }, days.Select(d => d.Date));
    }

    [Fact]
    public void Empty_match_suggests_the_nearest_open_slots()
    {
        var open = SlotGenerator.Generate(SaturdayMorning(60), Denver, Now);
        var ranges = Matcher.ToInstants([new LocalRange(L("2026-11-07T14:00:00"), L("2026-11-07T17:00:00"))], Denver);

        Assert.Empty(Matcher.Within(open, ranges));
        var nearest = Matcher.Nearest(open, ranges, 2);
        Assert.Equal(new[] { new LocalTime(10, 0), new LocalTime(11, 0) }, nearest.Select(s => s.Start.InZone(Denver).TimeOfDay));
    }

    [Fact]
    public void Buyer_range_that_starts_in_a_dst_gap_is_read_leniently()
    {
        var ranges = Matcher.ToInstants([new LocalRange(L("2027-03-14T02:30:00"), L("2027-03-14T05:00:00"))], Denver);
        Assert.Equal(new LocalTime(3, 30), ranges[0].Start.InZone(Denver).TimeOfDay);
    }

    [Fact]
    public void Rules_validation_catches_bad_input()
    {
        var rules = new ShowingRules { SlotMinutes = 5, Weekly = [new() { DayOfWeek = "Funday", Start = "10:00", End = "09:00" }], BlackoutDates = ["11/26"] };
        var problems = rules.Validate().ToList();
        Assert.Contains(problems, p => p.Contains("slotMinutes"));
        Assert.Contains(problems, p => p.Contains("Funday"));
        Assert.Contains(problems, p => p.Contains("end after"));
        Assert.Contains(problems, p => p.Contains("Blackout"));
    }

    [Fact]
    public void Overlapping_windows_on_the_same_day_are_rejected()
    {
        var rules = new ShowingRules { Weekly = [new() { DayOfWeek = "Saturday", Start = "09:00", End = "11:00" }, new() { DayOfWeek = "Saturday", Start = "10:00", End = "12:00" }] };
        Assert.Contains(rules.Validate(), p => p.Contains("overlap"));
    }
}
