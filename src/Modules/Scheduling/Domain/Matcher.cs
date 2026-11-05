using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Scheduling.Domain;

/// <summary>A buyer availability range in the buyer's own zone, e.g. 2026-11-07T09:00 to 12:00 America/Toronto.</summary>
public sealed record LocalRange(LocalDateTime Start, LocalDateTime End);

public sealed record DayGroup<T>(LocalDate Date, IReadOnlyList<T> Slots);

public sealed record ListingMatch<TListing>(TListing Listing, IReadOnlyList<Slot> Slots);

/// <summary>Intersects open slots with a buyer's availability and ranks listings.</summary>
public static class Matcher
{
    private static readonly LocalDateTimePattern LocalPattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");

    public static bool TryParseLocal(string s, out LocalDateTime value)
    {
        var r = LocalPattern.Parse(s);
        if (!r.Success) r = LocalDateTimePattern.ExtendedIso.Parse(s);
        value = r.Success ? r.Value : default;
        return r.Success;
    }

    /// <summary>Converts the buyer's local ranges to instants in the buyer's zone. A range that starts in a
    /// DST gap starts at the first valid instant after it (lenient), which is what a person means.</summary>
    public static IReadOnlyList<Interval> ToInstants(IEnumerable<LocalRange> ranges, DateTimeZone buyerZone) =>
        ranges.Where(r => r.End > r.Start)
            .Select(r => new Interval(r.Start.InZoneLeniently(buyerZone).ToInstant(), r.End.InZoneLeniently(buyerZone).ToInstant()))
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

    /// <summary>Slots that fit entirely inside at least one availability range.</summary>
    public static IReadOnlyList<Slot> Within(IEnumerable<Slot> slots, IReadOnlyList<Interval> ranges) =>
        slots.Where(s => ranges.Any(r => r.Start <= s.Start && s.End <= r.End)).ToList();

    /// <summary>
    /// Ranks listings with at least one matching slot. Without <paramref name="distance"/>: most matching slots first,
    /// ties keep the incoming order. With it (a "near" search): closest first, as $geoNear returned them, and the number
    /// of matching slots breaks ties between listings at the same distance.
    /// </summary>
    public static IReadOnlyList<ListingMatch<T>> Rank<T>(IEnumerable<(T Listing, IReadOnlyList<Slot> Open)> candidates, IReadOnlyList<Interval> ranges,
        Func<T, double?>? distance = null)
    {
        var matched = candidates.Select((c, i) => (Match: new ListingMatch<T>(c.Listing, Within(c.Open, ranges)), Index: i))
            .Where(x => x.Match.Slots.Count > 0);
        var ordered = distance is null
            ? matched.OrderByDescending(x => x.Match.Slots.Count).ThenBy(x => x.Index)
            : matched.OrderBy(x => distance(x.Match.Listing) ?? double.MaxValue).ThenByDescending(x => x.Match.Slots.Count).ThenBy(x => x.Index);
        return ordered.Select(x => x.Match).ToList();
    }

    /// <summary>Groups slots by calendar day in the viewer's zone: 11 p.m. in Denver is already tomorrow in Toronto.</summary>
    public static IReadOnlyList<DayGroup<Slot>> GroupByDay(IEnumerable<Slot> slots, DateTimeZone viewerZone) =>
        slots.OrderBy(s => s.Start)
            .GroupBy(s => s.Start.InZone(viewerZone).Date)
            .Select(g => new DayGroup<Slot>(g.Key, g.ToList()))
            .ToList();

    /// <summary>When nothing matches: the open slots closest in time to any of the buyer's ranges.</summary>
    public static IReadOnlyList<Slot> Nearest(IEnumerable<Slot> open, IReadOnlyList<Interval> ranges, int count) =>
        ranges.Count == 0 ? open.Take(count).ToList() :
        open.Select(s => (Slot: s, Gap: ranges.Min(r => Gap(s, r))))
            .OrderBy(x => x.Gap).ThenBy(x => x.Slot.Start)
            .Take(count).Select(x => x.Slot).OrderBy(s => s.Start).ToList();

    public static Duration Gap(Slot s, Interval r) =>
        s.End <= r.Start ? r.Start - s.End : r.End <= s.Start ? s.Start - r.End : Duration.Zero;
}
