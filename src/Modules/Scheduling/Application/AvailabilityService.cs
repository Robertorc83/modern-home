using HomeShowing.BuildingBlocks.Results;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Contracts;
using HomeShowing.Scheduling.Domain;
using HomeShowing.Scheduling.Infrastructure;
using NodaTime;

namespace HomeShowing.Scheduling.Application;

/// <summary>Open slots and buyer matching. Read only; shared by REST and MCP.</summary>
public sealed class AvailabilityService(
    IListingCatalog listings, ShowingRepository showings, SlotHoldRepository holds, IClock clock, ITenantContext tenant)
{
    public const int MaxRanges = 60;

    public static DateTimeZone? Zone(string? id) => string.IsNullOrWhiteSpace(id) ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(id);

    /// <summary>Open slots for many listings, with booked showings and other buyers' live holds removed.</summary>
    public async Task<Dictionary<string, IReadOnlyList<Slot>>> OpenSlotsAsync(IReadOnlyList<ListingDto> list, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var ids = list.Select(l => l.Id).ToList();
        var busy = await showings.ActiveByListingAsync(ids, now, ct);
        var buyerId = tenant.IsInRole(Roles.Buyer) ? tenant.UserId : null;
        var held = await holds.LiveByListingAsync(ids, buyerId, now, ct);
        return list.ToDictionary(l => l.Id, l =>
            SlotGenerator.Generate(l.ShowingRules, Zone(l.TimeZone) ?? DateTimeZone.Utc, now, busy[l.Id].Concat(held[l.Id])));
    }

    public async Task<IReadOnlyList<Slot>> OpenSlotsAsync(ListingDto listing, CancellationToken ct) =>
        (await OpenSlotsAsync([listing], ct))[listing.Id];

    public async Task<Result<ListingSlotsView>> GetListingSlotsAsync(string listingId, string? viewerZone, Instant? from, Instant? to, CancellationToken ct)
    {
        var listing = await listings.FindAsync(listingId, ct);
        if (listing is null || (tenant.IsInRole(Roles.Buyer) && listing.Status != ListingStatus.Active)) return Error.NotFound("Listing");
        var listingZone = Zone(listing.TimeZone) ?? DateTimeZone.Utc;
        var viewer = Zone(viewerZone) ?? listingZone;
        var open = (await OpenSlotsAsync(listing, ct)).Where(s => (from is null || s.Start >= from) && (to is null || s.End <= to));
        return new ListingSlotsView(listing.Id, listing.TimeZone, viewer.Id, SlotFormat.Days(open, viewer, listingZone));
    }

    public async Task<Result<MatchResponse>> MatchAsync(MatchRequest req, CancellationToken ct)
    {
        var buyerZone = Zone(req.TimeZone);
        if (buyerZone is null) return Error.Validation($"timeZone '{req.TimeZone}' is not an IANA zone.");
        if (req.Ranges.Count == 0) return Error.Validation("Add at least one availability range.");
        if (req.Ranges.Count > MaxRanges) return Error.Validation($"At most {MaxRanges} ranges.");
        var local = new List<LocalRange>();
        foreach (var r in req.Ranges)
        {
            if (!Matcher.TryParseLocal(r.Start, out var s) || !Matcher.TryParseLocal(r.End, out var e))
                return Error.Validation($"Range {r.Start} - {r.End} must use yyyy-MM-ddTHH:mm local times.");
            if (e <= s) return Error.Validation($"Range {r.Start} - {r.End} ends before it starts.");
            local.Add(new LocalRange(s, e));
        }
        var ranges = Matcher.ToInstants(local, buyerZone);

        var query = new ListingQuery
        {
            ActiveOnly = true, City = req.City, MaxPrice = req.MaxPrice, MinBeds = req.MinBeds, PropertyType = req.PropertyType,
            Near = req.Near, NearLat = req.NearLat, NearLng = req.NearLng, RadiusKm = req.RadiusKm,
            Ids = req.ListingId is null ? null : [req.ListingId],
        };
        if (!string.IsNullOrWhiteSpace(req.Near) && req.NearLat is null && !Listings.Domain.Places.TryResolve(req.Near, out _))
            return Error.Validation($"Unknown place '{req.Near}'. Try: {string.Join(", ", Listings.Domain.Places.Names.Take(10))}.");

        var hits = await listings.SearchAsync(query, ct);
        var open = await OpenSlotsAsync(hits.Select(h => h.Listing).ToList(), ct);
        var ranked = Matcher.Rank(hits.Select(h => (h, open[h.Listing.Id])), ranges);

        var results = ranked.Select(m =>
        {
            var lz = Zone(m.Listing.Listing.TimeZone) ?? DateTimeZone.Utc;
            return new MatchResult(m.Listing.Listing, m.Listing.DistanceKm, m.Slots.Count, SlotFormat.Days(m.Slots, buyerZone, lz));
        }).ToList();

        var suggestions = new List<Suggestion>();
        if (results.Count == 0)
        {
            // An empty match is still useful: the open slots closest to what the buyer asked for.
            suggestions = hits
                .Select(h => (Hit: h, Near: Matcher.Nearest(open[h.Listing.Id], ranges, 3)))
                .Where(x => x.Near.Count > 0)
                .OrderBy(x => x.Near.Min(s => ranges.Min(r => Matcher.Gap(s, r))))
                .Take(5)
                .Select(x => new Suggestion(x.Hit.Listing, x.Hit.DistanceKm,
                    x.Near.Select(s => SlotFormat.View(s, buyerZone, Zone(x.Hit.Listing.TimeZone) ?? DateTimeZone.Utc)).ToList()))
                .ToList();
        }
        return new MatchResponse(buyerZone.Id, results, suggestions);
    }
}
