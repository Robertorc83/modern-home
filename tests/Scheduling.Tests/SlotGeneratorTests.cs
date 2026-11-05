using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Domain;
using NodaTime;
using NodaTime.Text;

namespace Scheduling.Tests;

public class SlotGeneratorTests
{
    private static readonly DateTimeZone Denver = DateTimeZoneProviders.Tzdb["America/Denver"];
    private static readonly DateTimeZone Toronto = DateTimeZoneProviders.Tzdb["America/Toronto"];

    private static Instant At(string iso) => InstantPattern.ExtendedIso.Parse(iso).Value;

    private static ShowingRules Rules(int slot = 30, int buffer = 0, int lead = 0, int horizon = 7, params (string Day, string Start, string End)[] windows) => new()
    {
        SlotMinutes = slot, BufferMinutes = buffer, LeadTimeHours = lead, HorizonDays = horizon,
        Weekly = windows.Select(w => new WeeklyWindow { DayOfWeek = w.Day, Start = w.Start, End = w.End }).ToList(),
    };

    // Monday 2026-11-02 08:00 in Denver (MST, UTC-7)
    private static readonly Instant MondayMorning = At("2026-11-02T15:00:00Z");

    [Fact]
    public void Basic_window_is_cut_into_slots_and_converted_to_utc()
    {
        var slots = SlotGenerator.Generate(Rules(30, windows: ("Saturday", "09:00", "11:00")), Denver, MondayMorning);

        Assert.Equal(4, slots.Count);
        Assert.Equal(At("2026-11-07T16:00:00Z"), slots[0].Start); // 09:00 MST
        Assert.Equal(At("2026-11-07T16:30:00Z"), slots[0].End);
        Assert.Equal(At("2026-11-07T17:30:00Z"), slots[^1].Start); // 10:30 MST, ends exactly at 11:00
    }

    [Fact]
    public void Slot_that_does_not_fit_the_end_of_the_window_is_dropped()
    {
        var slots = SlotGenerator.Generate(Rules(45, windows: ("Saturday", "09:00", "10:00")), Denver, MondayMorning);
        Assert.Single(slots);
    }

    [Fact]
    public void Buffer_spaces_the_grid()
    {
        // 30 min slots + 15 min buffer in 09:00-11:00: 09:00, 09:45, 10:30
        var slots = SlotGenerator.Generate(Rules(30, buffer: 15, windows: ("Saturday", "09:00", "11:00")), Denver, MondayMorning);
        Assert.Equal(new[] { "09:00", "09:45", "10:30" }, slots.Select(s => s.Start.InZone(Denver).TimeOfDay.ToString("HH:mm", null)));
    }

    [Fact]
    public void Busy_showing_removes_its_slot_and_buffer_protects_neighbours()
    {
        var rules = Rules(30, buffer: 0, windows: ("Saturday", "09:00", "11:00"));
        var busy = new Interval(At("2026-11-07T16:30:00Z"), At("2026-11-07T17:00:00Z")); // 09:30-10:00
        var noBuffer = SlotGenerator.Generate(rules, Denver, MondayMorning, [busy]);
        Assert.Equal(3, noBuffer.Count);
        Assert.DoesNotContain(noBuffer, s => s.Start == busy.Start);

        // Rules changed to a 15 min buffer after the booking: neighbours that touch the busy slot are not offered.
        var withBuffer = SlotGenerator.Generate(Rules(30, buffer: 15, windows: ("Saturday", "09:00", "11:00")), Denver, MondayMorning, [busy]);
        Assert.All(withBuffer, s => Assert.True(s.End <= busy.Start - Duration.FromMinutes(15) || s.Start >= busy.End + Duration.FromMinutes(15)));
    }

    [Fact]
    public void Blackout_dates_are_skipped()
    {
        var rules = Rules(60, horizon: 14, windows: ("Saturday", "09:00", "10:00"));
        rules.BlackoutDates = ["2026-11-07"];
        var slots = SlotGenerator.Generate(rules, Denver, MondayMorning);
        Assert.Single(slots);
        Assert.Equal(new LocalDate(2026, 11, 14), slots[0].Start.InZone(Denver).Date);
    }

    [Fact]
    public void Lead_time_hides_slots_that_start_too_soon()
    {
        // Monday 08:00 local, lead time 3 h: the 09:00 and 10:00 slots are too soon, 11:00 is the first.
        var slots = SlotGenerator.Generate(Rules(60, lead: 3, horizon: 1, windows: ("Monday", "09:00", "13:00")), Denver, MondayMorning);
        Assert.Equal(new[] { "11:00", "12:00" }, slots.Select(s => s.Start.InZone(Denver).TimeOfDay.ToString("HH:mm", null)));
    }

    [Fact]
    public void Horizon_limits_how_far_ahead_slots_exist()
    {
        var rules = Rules(60, horizon: 3, windows: ("Saturday", "09:00", "10:00"));
        Assert.Empty(SlotGenerator.Generate(rules, Denver, MondayMorning)); // Mon, Tue, Wed only
    }

    [Fact]
    public void Spring_forward_skips_the_missing_hour_and_keeps_utc_correct()
    {
        // 2027-03-14 02:00 MST -> 03:00 MDT in Denver. Window 01:00-04:00, 60 min slots.
        var now = At("2027-03-10T12:00:00Z");
        var slots = SlotGenerator.Generate(Rules(60, horizon: 7, windows: ("Sunday", "01:00", "04:00")), Denver, now);

        Assert.Equal(new[] { At("2027-03-14T08:00:00Z"), At("2027-03-14T09:00:00Z") }, slots.Select(s => s.Start));
        Assert.Equal("03:00", slots[1].Start.InZone(Denver).TimeOfDay.ToString("HH:mm", null)); // 02:00 never happened
        Assert.All(slots, s => Assert.Equal(Duration.FromMinutes(60), s.End - s.Start));
    }

    [Fact]
    public void Saturday_morning_moves_by_one_hour_in_utc_across_the_transition()
    {
        var now = At("2027-03-08T12:00:00Z");
        var slots = SlotGenerator.Generate(Rules(60, horizon: 14, windows: ("Saturday", "10:00", "11:00")), Denver, now);
        Assert.Equal(new[] { At("2027-03-13T17:00:00Z"), At("2027-03-20T16:00:00Z") }, slots.Select(s => s.Start));
    }

    [Fact]
    public void Fall_back_offers_the_repeated_hour_once()
    {
        // 2026-11-01 02:00 MDT -> 01:00 MST. Window 00:30-02:30 with 30 min slots.
        var now = At("2026-10-28T12:00:00Z");
        var slots = SlotGenerator.Generate(Rules(30, horizon: 7, windows: ("Sunday", "00:30", "02:30")), Denver, now);
        var starts = slots.Select(s => s.Start).ToList();
        Assert.Equal(starts.Distinct().Count(), starts.Count);
        Assert.Equal(At("2026-11-01T06:30:00Z"), starts[0]);   // 00:30 MDT
        Assert.Equal(At("2026-11-01T07:00:00Z"), starts[1]);   // 01:00 MDT (earlier offset)
        Assert.Equal(At("2026-11-01T09:00:00Z"), starts[^1]);  // 02:00 MST
    }

    [Fact]
    public void Slots_are_generated_in_the_listing_zone_not_the_server_zone()
    {
        var slots = SlotGenerator.Generate(Rules(60, windows: ("Saturday", "09:00", "10:00")), Toronto, MondayMorning);
        Assert.Equal(At("2026-11-07T14:00:00Z"), slots.Single().Start); // 09:00 EST
    }
}

public class SlotGeneratorOverlapTests
{
    private static readonly DateTimeZone Denver = DateTimeZoneProviders.Tzdb["America/Denver"];
    private static Instant At(string iso) => InstantPattern.ExtendedIso.Parse(iso).Value;

    [Fact]
    public void Slots_never_overlap_across_spring_forward_even_when_the_step_does_not_divide_an_hour()
    {
        // 60 min slots + 30 min buffer, window 00:00-06:00 on 2027-03-14 (02:00 MST -> 03:00 MDT).
        var rules = new ShowingRules
        {
            SlotMinutes = 60, BufferMinutes = 30, HorizonDays = 7, LeadTimeHours = 0,
            Weekly = [new WeeklyWindow { DayOfWeek = "Sunday", Start = "00:00", End = "06:00" }],
        };
        var slots = SlotGenerator.Generate(rules, Denver, At("2027-03-10T12:00:00Z"));
        for (var i = 1; i < slots.Count; i++)
            Assert.True(slots[i].Start >= slots[i - 1].End + Duration.FromMinutes(30), $"{slots[i - 1]} overlaps {slots[i]}");
        Assert.DoesNotContain(slots, s => s.Start.InZone(Denver).Hour == 2);
    }

    [Fact]
    public void A_window_ending_at_midnight_offers_its_last_slot()
    {
        var rules = new ShowingRules
        {
            SlotMinutes = 30, HorizonDays = 7, LeadTimeHours = 0,
            Weekly = [new WeeklyWindow { DayOfWeek = "Saturday", Start = "22:00", End = "24:00" }],
        };
        var slots = SlotGenerator.Generate(rules, Denver, At("2026-11-02T15:00:00Z"));
        Assert.Equal(4, slots.Count);
        Assert.Equal(new LocalTime(23, 30), slots[^1].Start.InZone(Denver).TimeOfDay);
    }

    [Fact]
    public void Claims_cover_every_five_minute_bucket_of_a_slot()
    {
        var buckets = SlotClaim.Buckets(At("2026-11-07T16:00:00Z"), At("2026-11-07T16:45:00Z")).ToList();
        Assert.Equal(9, buckets.Count);
        Assert.Equal(At("2026-11-07T16:00:00Z"), buckets[0]);
        Assert.Equal(At("2026-11-07T16:40:00Z"), buckets[^1]);
        // A slot starting off the 5 minute grid still claims the bucket it starts in.
        Assert.Equal(At("2026-11-07T16:00:00Z"), SlotClaim.Buckets(At("2026-11-07T16:03:00Z"), At("2026-11-07T16:10:00Z")).First());
    }
}
