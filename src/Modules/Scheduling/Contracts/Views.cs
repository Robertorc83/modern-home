using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Domain;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Scheduling.Contracts;

public sealed record SlotView(Instant StartUtc, Instant EndUtc, string LocalStart, string LocalEnd, string ListingLocalStart, string Label, string ListingLabel);
public sealed record DayGroupView(string Date, string Label, IReadOnlyList<SlotView> Slots);
public sealed record ListingSlotsView(string ListingId, string TimeZone, string ViewerTimeZone, IReadOnlyList<DayGroupView> Days);

public sealed record RangeInput(string Start, string End);

public sealed class MatchRequest
{
    public string TimeZone { get; set; } = "America/Denver";
    public List<RangeInput> Ranges { get; set; } = [];
    public string? City { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MinBeds { get; set; }
    public PropertyType? PropertyType { get; set; }
    public string? Near { get; set; }
    public double? NearLat { get; set; }
    public double? NearLng { get; set; }
    public double? RadiusKm { get; set; }
    public string? ListingId { get; set; }
}

public sealed record MatchResult(ListingDto Listing, double? DistanceKm, int MatchCount, IReadOnlyList<DayGroupView> Days);
public sealed record Suggestion(ListingDto Listing, double? DistanceKm, IReadOnlyList<SlotView> Slots);
public sealed record MatchResponse(string TimeZone, IReadOnlyList<MatchResult> Results, IReadOnlyList<Suggestion> Suggestions);

public sealed record ShowingView(
    string Id, string ListingId, string ListingAddress, string ListingCity, string ListingTimeZone,
    string BuyerId, string BuyerName, string AgentId, string AgentName,
    Instant SlotStartUtc, Instant SlotEndUtc, ShowingStatus Status, Instant CreatedAt, Instant UpdatedAt,
    string ListingLocalLabel, string? Source);

public sealed record HoldView(string Id, string ListingId, Instant SlotStartUtc, Instant ExpiresAt);

public sealed record BookRequest(string ListingId, string SlotStart, string? Source = null, string? TimeZone = null);
public sealed record HoldRequest(string ListingId, string SlotStart);

public static class SlotFormat
{
    private static readonly ZonedDateTimePattern Time = ZonedDateTimePattern.CreateWithInvariantCulture("h:mm tt", null);
    private static readonly ZonedDateTimePattern TimeZoned = ZonedDateTimePattern.CreateWithInvariantCulture("h:mm tt x", null);
    private static readonly ZonedDateTimePattern Full = ZonedDateTimePattern.CreateWithInvariantCulture("ddd, MMM d, h:mm tt x", null);
    private static readonly LocalDateTimePattern Local = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");
    private static readonly LocalDatePattern DayLabel = LocalDatePattern.CreateWithInvariantCulture("ddd, MMM d");

    public static SlotView View(Slot s, DateTimeZone viewer, DateTimeZone listing)
    {
        var vs = s.Start.InZone(viewer);
        var ls = s.Start.InZone(listing);
        return new SlotView(s.Start, s.End, Local.Format(vs.LocalDateTime), Local.Format(s.End.InZone(viewer).LocalDateTime),
            Local.Format(ls.LocalDateTime), Time.Format(vs), TimeZoned.Format(ls));
    }

    public static IReadOnlyList<DayGroupView> Days(IEnumerable<Slot> slots, DateTimeZone viewer, DateTimeZone listing) =>
        Matcher.GroupByDay(slots, viewer)
            .Select(g => new DayGroupView(LocalDatePattern.Iso.Format(g.Date), DayLabel.Format(g.Date), g.Slots.Select(s => View(s, viewer, listing)).ToList()))
            .ToList();

    public static string Label(Instant start, DateTimeZone zone) => Full.Format(start.InZone(zone));

    public static ShowingView View(Showing s)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(s.ListingTimeZone) ?? DateTimeZone.Utc;
        return new ShowingView(s.Id, s.ListingId, s.ListingAddress, s.ListingCity, s.ListingTimeZone, s.BuyerId, s.BuyerName,
            s.AgentId, s.AgentName, s.SlotStartUtc, s.SlotEndUtc, s.Status, s.CreatedAt, s.UpdatedAt, Label(s.SlotStartUtc, zone), s.Source);
    }
}
