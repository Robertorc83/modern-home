using System.ComponentModel;
using System.Globalization;
using System.Text;
using HomeShowing.Ai.Application;
using HomeShowing.BuildingBlocks.Results;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Application;
using HomeShowing.Scheduling.Contracts;
using HomeShowing.Scheduling.Domain;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Mcp;

/// <summary>Buyer tools. Each one calls the same application service as the matching REST endpoint.</summary>
[McpServerToolType]
public sealed class BuyerTools
{
    [McpServerTool(Name = "search_listings", Title = "Search listings", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Search active homes for sale in the buyer's brokerage. All filters are optional. 'near' is a place name such as Denver, LoDo, Cherry Creek, Austin or Toronto; results near a place are sorted by distance.")]
    public static async Task<string> SearchListings(
        IListingCatalog catalog,
        [Description("City name, e.g. Denver")] string? city = null,
        [Description("Maximum price in dollars")] decimal? maxPrice = null,
        [Description("Minimum bedrooms")] int? minBeds = null,
        [Description("House, Condo or Townhouse")] PropertyType? propertyType = null,
        [Description("Place name to search around, e.g. LoDo")] string? near = null,
        [Description("Search radius in km when 'near' is set (default 25)")] double? radiusKm = null,
        CancellationToken ct = default)
    {
        // Same validation as GET /listings: an unknown place or a bad radius is a tool error, not "no results".
        var r = await catalog.ListAsync(new ListingQuery { ActiveOnly = true, City = city, MaxPrice = maxPrice, MinBeds = minBeds, PropertyType = propertyType, Near = near, RadiusKm = radiusKm }, ct);
        if (!r.IsSuccess) throw new McpException(r.Error!.Detail ?? r.Error.Title);
        var hits = r.Value;
        if (hits.Count == 0) return "No active listings match those filters.";
        var sb = new StringBuilder($"{hits.Count} listing(s):\n");
        foreach (var h in hits) sb.AppendLine("- " + Format.Listing(h.Listing, h.DistanceKm));
        return sb.ToString();
    }

    [McpServerTool(Name = "find_open_slots", Title = "Find open showing slots", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Find open showing slots that fit the buyer's availability. Give availability as plain text (e.g. 'Saturday morning', 'weekday evenings after 6 except Thursday') or as explicit local ranges. Optionally narrow to one listing or filter like search_listings. Times are returned in the buyer's time zone, each with the exact slotStart to pass to book_showing.")]
    public static async Task<string> FindOpenSlots(
        AvailabilityService availability, AvailabilityParsingService parser, ITenantContext tenant, IClock clock,
        [Description("Plain text availability, e.g. 'Saturday morning'")] string? availabilityText = null,
        [Description("Explicit ranges in the buyer's time zone, e.g. [{\"start\":\"2026-11-07T09:00\",\"end\":\"2026-11-07T12:00\"}]")] RangeInput[]? ranges = null,
        [Description("Buyer IANA time zone; defaults to the buyer's profile zone")] string? timeZone = null,
        [Description("Only this listing")] string? listingId = null,
        string? city = null, decimal? maxPrice = null, int? minBeds = null, PropertyType? propertyType = null,
        [Description("Place name to search around")] string? near = null, double? radiusKm = null,
        CancellationToken ct = default)
    {
        var zone = timeZone ?? tenant.TimeZone ?? "America/Denver";
        var rangeList = ranges?.ToList() ?? [];
        var preface = "";
        if (rangeList.Count == 0 && !string.IsNullOrWhiteSpace(availabilityText))
        {
            var parsed = await parser.ParseAsync(new ParseRequest(availabilityText, zone), ct);
            if (!parsed.IsSuccess) throw new McpException(parsed.Error!.Detail ?? parsed.Error.Title);
            rangeList = parsed.Value.Ranges.Select(r => new RangeInput(r.Start, r.End)).ToList();
            preface = $"Read \"{availabilityText}\" as: {parsed.Value.Summary} ({zone}, next 14 days, parsed by {parsed.Value.Provider}).\n";
            if (rangeList.Count == 0) throw new McpException($"Could not read any times from \"{availabilityText}\". Try e.g. 'Saturday 9-12'.");
        }
        if (rangeList.Count == 0)
        {
            var now = clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) ?? DateTimeZone.Utc);
            rangeList = [new RangeInput(now.LocalDateTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), now.LocalDateTime.PlusDays(7).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture))];
            preface = "No availability given, so showing the next 7 days.\n";
        }

        var result = await availability.MatchAsync(new MatchRequest
        {
            TimeZone = zone, Ranges = rangeList, ListingId = listingId, City = city, MaxPrice = maxPrice, MinBeds = minBeds,
            PropertyType = propertyType, Near = near, RadiusKm = radiusKm,
        }, ct);
        if (!result.IsSuccess) throw new McpException(result.Error!.Detail ?? result.Error.Title);

        var sb = new StringBuilder(preface);
        var r = result.Value;
        if (r.Results.Count == 0)
        {
            sb.AppendLine("No open slots inside that availability.");
            if (r.Suggestions.Count > 0)
            {
                sb.AppendLine("Nearest open slots instead:");
                foreach (var s in r.Suggestions)
                {
                    sb.AppendLine("- " + Format.Listing(s.Listing, s.DistanceKm));
                    sb.AppendLine("    " + string.Join(", ", s.Slots.Select(Format.Slot)));
                }
            }
            return sb.ToString();
        }
        sb.AppendLine($"{r.Results.Count} listing(s) with open slots (times in {r.TimeZone}):");
        foreach (var m in r.Results.Take(8))
        {
            sb.AppendLine($"- {Format.Listing(m.Listing, m.DistanceKm)} · {m.MatchCount} matching slot(s)");
            foreach (var d in m.Days.Take(4))
                sb.AppendLine($"    {d.Label}: {string.Join(", ", d.Slots.Take(8).Select(Format.Slot))}{(d.Slots.Count > 8 ? $" (+{d.Slots.Count - 8} more)" : "")}");
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "book_showing", Title = "Book a showing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Request a showing for the buyer at one open slot. Use a listingId and the exact slotStart (UTC, e.g. 2026-11-07T16:00:00Z) returned by find_open_slots. The listing agent confirms it, unless the listing confirms automatically.")]
    public static async Task<string> BookShowing(
        BookingService booking, ITenantContext tenant,
        [Description("Listing id")] string listingId,
        [Description("Slot start exactly as returned by find_open_slots, e.g. 2026-11-07T16:00:00Z")] string slotStart,
        CancellationToken ct = default)
    {
        var r = await booking.BookAsync(new BookRequest(listingId, slotStart, "mcp", tenant.TimeZone), ct);
        if (r.IsSuccess)
        {
            var s = r.Value;
            return $"{(s.Status == ShowingStatus.Confirmed ? "Booked and confirmed" : "Requested")}: {s.ListingAddress}, {s.ListingCity} on {Format.When(s.SlotStartUtc, tenant.TimeZone, s.ListingTimeZone)}. " +
                   $"Agent: {s.AgentName}. Status: {s.Status}. Showing id: {s.Id}.";
        }
        throw new McpException(Format.Error(r.Error!, tenant.TimeZone));
    }

    [McpServerTool(Name = "list_my_showings", Title = "List my showings", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List the buyer's showings (requested, confirmed, declined, cancelled), soonest first.")]
    public static async Task<string> ListMyShowings(BookingService booking, ITenantContext tenant, CancellationToken ct = default)
    {
        var list = await booking.MineAsync(ct);
        if (list.Count == 0) return "You have no showings yet.";
        return string.Join("\n", list.Select(s => $"- {s.Status}: {s.ListingAddress}, {s.ListingCity} on {Format.When(s.SlotStartUtc, tenant.TimeZone, s.ListingTimeZone)} (showing id {s.Id})"));
    }

    [McpServerTool(Name = "cancel_showing", Title = "Cancel a showing", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Cancel one of the buyer's requested or confirmed showings. This frees the slot for other buyers.")]
    public static async Task<string> CancelShowing(BookingService booking, ITenantContext tenant, [Description("Showing id")] string showingId, CancellationToken ct = default)
    {
        var r = await booking.CancelAsync(showingId, ct);
        return r.IsSuccess ? $"Cancelled the showing at {r.Value.ListingAddress} on {Format.When(r.Value.SlotStartUtc, tenant.TimeZone, r.Value.ListingTimeZone)}." : throw new McpException(Format.Error(r.Error!, tenant.TimeZone));
    }
}

/// <summary>Agent tools.</summary>
[McpServerToolType]
public sealed class AgentTools
{
    [McpServerTool(Name = "list_showing_requests", Title = "List showing requests", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("List showings on the agent's listings. Defaults to pending requests; pass status Confirmed, Declined, Cancelled or All.")]
    public static async Task<string> ListShowingRequests(BookingService booking, [Description("Requested (default), Confirmed, Declined, Cancelled or All")] string? status = "Requested", CancellationToken ct = default)
    {
        ShowingStatus? filter = string.Equals(status, "All", StringComparison.OrdinalIgnoreCase) ? null
            : Enum.TryParse<ShowingStatus>(status ?? "Requested", true, out var st) ? st : ShowingStatus.Requested;
        var r = await booking.InboxAsync(filter, ct);
        if (!r.IsSuccess) throw new McpException(r.Error!.Detail ?? r.Error.Title);
        if (r.Value.Count == 0) return $"No {(filter?.ToString() ?? "")} showings.".Replace("  ", " ");
        return string.Join("\n", r.Value.Select(s => $"- {s.Status}: {s.BuyerName} at {s.ListingAddress} on {s.ListingLocalLabel} (showing id {s.Id}, via {s.Source ?? "web"})"));
    }

    [McpServerTool(Name = "respond_to_request", Title = "Confirm or decline a showing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Confirm or decline a requested showing on one of the agent's listings. The buyer is notified.")]
    public static async Task<string> RespondToRequest(BookingService booking,
        [Description("Showing id")] string showingId,
        [Description("confirm or decline")] string decision,
        CancellationToken ct = default)
    {
        var r = decision.Trim().ToLowerInvariant() switch
        {
            "confirm" or "approve" or "accept" => await booking.ConfirmAsync(showingId, ct),
            "decline" or "reject" => await booking.DeclineAsync(showingId, ct),
            _ => throw new McpException("decision must be 'confirm' or 'decline'."),
        };
        return r.IsSuccess ? $"{r.Value.Status}: {r.Value.BuyerName} at {r.Value.ListingAddress} on {r.Value.ListingLocalLabel}." : throw new McpException(Format.Error(r.Error!, null));
    }

    [McpServerTool(Name = "get_listing_schedule", Title = "Get a listing's schedule", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Show the agent's listings, or for one listing its showing rules, booked showings and next open slots (in the listing's time zone).")]
    public static async Task<string> GetListingSchedule(IListingCatalog listings, BookingService booking, AvailabilityService availability, ITenantContext tenant,
        [Description("Listing id; omit to list your listings")] string? listingId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            var mine = await listings.ListAsync(new ListingQuery { Mine = true }, ct);
            return mine.IsSuccess && mine.Value.Count > 0 ? string.Join("\n", mine.Value.Select(h => "- " + Format.Listing(h.Listing, null))) : "You have no listings.";
        }
        var l = await listings.GetAsync(listingId, ct);
        if (!l.IsSuccess) throw new McpException("Listing not found.");
        var showings = await booking.ForListingAsync(listingId, ct);
        if (!showings.IsSuccess) throw new McpException(showings.Error!.Detail ?? showings.Error.Title);
        var slots = await availability.GetListingSlotsAsync(listingId, l.Value.TimeZone, null, null, ct);
        var rules = l.Value.ShowingRules;
        var sb = new StringBuilder($"{Format.Listing(l.Value, null)}\n");
        sb.AppendLine($"Rules: {rules.SlotMinutes} min slots, {rules.BufferMinutes} min buffer, {rules.LeadTimeHours} h lead time, {rules.HorizonDays} days ahead, auto confirm {(rules.AutoConfirm ? "on" : "off")}.");
        sb.AppendLine("Weekly windows: " + string.Join("; ", rules.Weekly.Select(w => $"{w.DayOfWeek[..3]} {w.Start}-{w.End}")) +
                      (rules.BlackoutDates.Count > 0 ? $". Blackouts: {string.Join(", ", rules.BlackoutDates)}" : ""));
        var active = showings.Value.Where(s => s.Status is ShowingStatus.Requested or ShowingStatus.Confirmed).ToList();
        sb.AppendLine(active.Count == 0 ? "No upcoming showings." : "Upcoming showings:\n" + string.Join("\n", active.Select(s => $"  - {s.Status}: {s.BuyerName} on {s.ListingLocalLabel} (id {s.Id})")));
        if (slots.IsSuccess)
            sb.AppendLine("Next open slots: " + string.Join("; ", slots.Value.Days.Take(3).Select(d => $"{d.Label}: {string.Join(", ", d.Slots.Take(6).Select(s => s.Label))}")));
        return sb.ToString();
    }
}

internal static class Format
{
    public static string Listing(ListingDto l, double? km) =>
        $"{l.Address}, {l.City}{(l.Neighborhood is null ? "" : $" ({l.Neighborhood})")} · ${l.Price:N0} · {l.Beds} bd / {l.Baths:0.#} ba {l.PropertyType}" +
        $"{(km is null ? "" : $" · {km:0.#} km away")} · listing id {l.Id}";

    public static string Slot(SlotView s) => $"{s.Label} [slotStart={InstantPattern.ExtendedIso.Format(s.StartUtc)}]";

    public static string When(Instant start, string? viewerZone, string listingZone)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(viewerZone ?? listingZone) ?? DateTimeZone.Utc;
        return SlotFormat.Label(start, zone);
    }

    public static string Error(Error e, string? zone)
    {
        var msg = e.Title + (e.Detail is null ? "" : " " + e.Detail);
        if (e.Extensions?.TryGetValue("alternatives", out var alt) == true && alt is IEnumerable<SlotView> slots && slots.Any())
            msg += " Nearest open slots: " + string.Join(", ", slots.Select(s => $"{s.ListingLabel} [slotStart={InstantPattern.ExtendedIso.Format(s.StartUtc)}]"));
        return msg;
    }
}
