using HomeShowing.Listings.Contracts;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Scheduling.Domain;

/// <summary>A bookable slot. Always UTC instants; the zone is applied only when displaying.</summary>
public readonly record struct Slot(Instant Start, Instant End)
{
    public Interval ToInterval() => new(Start, End);
}

/// <summary>
/// Expands a listing's weekly showing windows into concrete slots.
///
/// Rules are wall clock times in the listing's IANA zone, so expansion happens in local time and
/// each slot is converted to UTC on its own. That is what makes daylight saving correct:
/// <list type="bullet">
/// <item>Spring forward: a local start inside the gap (02:00-02:59 in the US) does not exist and is skipped,
/// instead of being shifted onto an hour the seller never offered.</item>
/// <item>Fall back: an ambiguous local start (01:00-01:59 twice) resolves to the earlier offset, so the
/// repeated hour is offered once, never twice.</item>
/// <item>Slot length is elapsed time (start instant + slotMinutes), and the slot must end inside the window in local time.</item>
/// </list>
/// Slots snap to a fixed grid: window start + n * (slotMinutes + bufferMinutes), stepping in elapsed time so
/// consecutive slots never overlap even across a transition. The grid is what makes "the same slot" mean the same
/// key for two buyers; the per bucket claims in <c>BookingService</c> are what make overlapping slots impossible
/// even after the grid changes.
/// </summary>
public static class SlotGenerator
{
    public static IReadOnlyList<Slot> Generate(ShowingRules rules, DateTimeZone zone, Instant now, IEnumerable<Interval>? busy = null)
    {
        var slotLength = Duration.FromMinutes(rules.SlotMinutes);
        var step = Period.FromMinutes(rules.SlotMinutes + rules.BufferMinutes);
        var stepElapsed = Duration.FromMinutes(rules.SlotMinutes + rules.BufferMinutes);
        var earliest = now + Duration.FromHours(rules.LeadTimeHours);
        var buffer = Duration.FromMinutes(rules.BufferMinutes);
        var blocked = (busy ?? []).Select(b => new Interval(b.Start - buffer, b.End + buffer)).ToList();
        var blackouts = rules.BlackoutDates
            .Select(d => LocalDatePattern.Iso.Parse(d))
            .Where(r => r.Success).Select(r => r.Value).ToHashSet();
        var byDay = rules.Weekly.GroupBy(w => w.Day).ToDictionary(g => g.Key, g => g.ToList());

        var today = now.InZone(zone).Date;
        var result = new List<Slot>();
        for (var i = 0; i < rules.HorizonDays; i++)
        {
            var date = today.PlusDays(i);
            if (blackouts.Contains(date) || !byDay.TryGetValue(date.DayOfWeek, out var windows)) continue;

            foreach (var w in windows)
            {
                var windowEnd = w.End == "24:00" ? date.PlusDays(1).AtMidnight() : date + w.EndTime;
                Instant? nextAllowed = null; // previous slot's end plus buffer, in elapsed time
                for (var localStart = date + w.StartTime; localStart.PlusMinutes(rules.SlotMinutes) <= windowEnd; localStart = localStart.Plus(step))
                {
                    var mapping = zone.MapLocal(localStart);
                    if (mapping.Count == 0) continue;                       // DST gap: this wall time never happens
                    var start = mapping.First().ToInstant();                // DST overlap: earlier offset, once
                    if (nextAllowed is { } n && start < n) continue;       // wall clock stepped back over the previous slot
                    var end = start + slotLength;
                    nextAllowed = start + stepElapsed;
                    if (end.InZone(zone).LocalDateTime > windowEnd) continue; // would run past the window after a transition
                    if (start < earliest) continue;                         // inside the lead time
                    var slot = new Slot(start, end);
                    if (blocked.Any(b => Overlaps(b, slot))) continue;     // taken (with buffer around it)
                    result.Add(slot);
                }
            }
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result.Distinct().ToList();
    }

    private static bool Overlaps(Interval a, Slot s) => a.Start < s.End && s.Start < a.End;
}
