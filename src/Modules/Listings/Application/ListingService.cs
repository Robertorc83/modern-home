using System.Text.Json;
using HomeShowing.BuildingBlocks.Audit;
using HomeShowing.BuildingBlocks.Results;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Listings.Contracts;
using HomeShowing.Listings.Domain;
using HomeShowing.Listings.Infrastructure;
using MongoDB.Driver.GeoJsonObjectModel;
using NodaTime;

namespace HomeShowing.Listings.Application;

/// <summary>The public face of the Listings module: other modules (Scheduling, MCP) depend on this, not on its storage.</summary>
public interface IListingCatalog
{
    /// <summary>A listing in the caller's tenant, or null (other tenants' ids look like they do not exist).</summary>
    Task<ListingDto?> FindAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<ListingSearchHit>> SearchAsync(ListingQuery query, CancellationToken ct);
}

public sealed class ListingService : IListingCatalog
{
    private readonly ListingRepository _repo;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    internal ListingService(ListingRepository repo, ITenantContext tenant, IClock clock, IAuditLog audit)
    {
        _repo = repo; _tenant = tenant; _clock = clock; _audit = audit;
    }

    public async Task<ListingDto?> FindAsync(string id, CancellationToken ct) => (await _repo.GetByIdAsync(id, ct))?.ToDto();

    public async Task<IReadOnlyList<ListingSearchHit>> SearchAsync(ListingQuery query, CancellationToken ct)
    {
        GeoPoint? near = null;
        if (query.NearLat is { } lat && query.NearLng is { } lng) near = new GeoPoint(lat, lng);
        else if (Places.TryResolve(query.Near, out var p)) near = p;
        var hits = await _repo.SearchAsync(query, null, near, ct);
        return hits.Select(h => new ListingSearchHit(h.Listing.ToDto(), h.DistanceKm)).ToList();
    }

    /// <summary>Role aware listing: buyers see active listings, agents can ask for their own, admins see all.</summary>
    public async Task<Result<IReadOnlyList<ListingSearchHit>>> ListAsync(ListingQuery query, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(query.Near) && query.NearLat is null && !Places.TryResolve(query.Near, out _))
            return Error.Validation($"Unknown place '{query.Near}'. Try one of: {string.Join(", ", Places.Names.Take(8))}.");
        if (_tenant.IsInRole(Roles.Buyer)) query.ActiveOnly = true;
        if (query.Mine && _tenant.IsInRole(Roles.Agent))
        {
            GeoPoint? near = null;
            var own = await _repo.SearchAsync(query, _tenant.UserId, near, ct);
            return own.Select(h => new ListingSearchHit(h.Listing.ToDto(), h.DistanceKm)).ToList();
        }
        return Result<IReadOnlyList<ListingSearchHit>>.Ok(await SearchAsync(query, ct));
    }

    public async Task<Result<ListingDto>> GetAsync(string id, CancellationToken ct)
    {
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null || (_tenant.IsInRole(Roles.Buyer) && l.Status != ListingStatus.Active)) return Error.NotFound("Listing");
        return l.ToDto();
    }

    public async Task<Result<ListingDto>> CreateAsync(ListingInput input, CancellationToken ct)
    {
        if (!_tenant.IsInRole(Roles.Agent)) return Error.Forbidden("Only agents create listings.");
        if (Validate(input) is { } err) return err;
        var now = _clock.GetCurrentInstant();
        var l = new Listing { OwnerUserId = _tenant.UserId, OwnerName = _tenant.UserName ?? "", CreatedAt = now, UpdatedAt = now };
        Apply(l, input);
        await _repo.AddAsync(l, ct);
        await _audit.RecordAsync("listing.created", l.Id, ct);
        return l.ToDto();
    }

    public async Task<Result<ListingDto>> UpdateAsync(string id, ListingInput input, CancellationToken ct)
    {
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null) return Error.NotFound("Listing");
        if (CanEdit(l) is { } denied) return denied;
        if (Validate(input) is { } err) return err;
        var rules = l.ShowingRules;
        Apply(l, input);
        if (input.ShowingRules is null) l.ShowingRules = rules;
        l.UpdatedAt = _clock.GetCurrentInstant();
        await _repo.SaveAsync(l, ct);
        await _audit.RecordAsync("listing.updated", l.Id, ct);
        return l.ToDto();
    }

    public async Task<Result<ListingDto>> UpdateRulesAsync(string id, ShowingRules rules, CancellationToken ct)
    {
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null) return Error.NotFound("Listing");
        if (CanEdit(l) is { } denied) return denied;
        var problems = rules.Validate().ToList();
        if (problems.Count > 0) return Error.Validation(string.Join(" ", problems));
        l.ShowingRules = Normalize(rules);
        l.UpdatedAt = _clock.GetCurrentInstant();
        await _repo.SaveAsync(l, ct);
        await _audit.RecordAsync("listing.rules_updated", l.Id, ct);
        return l.ToDto();
    }

    private Error? CanEdit(Listing l)
    {
        if (_tenant.IsInRole(Roles.BrokerageAdmin)) return null;
        if (_tenant.IsInRole(Roles.Agent) && l.OwnerUserId == _tenant.UserId) return null;
        return Error.Forbidden("Only the listing's agent can change it.");
    }

    private static Error? Validate(ListingInput i)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(i.Address)) problems.Add("address is required.");
        if (string.IsNullOrWhiteSpace(i.City)) problems.Add("city is required.");
        if (i.Price <= 0) problems.Add("price must be positive.");
        if (i.Beds is < 0 or > 30) problems.Add("beds must be between 0 and 30.");
        if (i.Baths is < 0 or > 30) problems.Add("baths must be between 0 and 30.");
        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(i.TimeZone ?? "") is null) problems.Add($"timeZone '{i.TimeZone}' is not an IANA zone.");
        if (i.Location is null) problems.Add("location is required.");
        else if (i.Location.Lat is < -90 or > 90 || i.Location.Lng is < -180 or > 180) problems.Add("location is out of range.");
        if (i.ShowingRules is not null) problems.AddRange(i.ShowingRules.Validate());
        return problems.Count > 0 ? Error.Validation(string.Join(" ", problems)) : null;
    }

    private static void Apply(Listing l, ListingInput i)
    {
        l.Status = i.Status;
        l.Address = i.Address.Trim();
        l.City = i.City.Trim();
        l.Neighborhood = string.IsNullOrWhiteSpace(i.Neighborhood) ? null : i.Neighborhood.Trim();
        l.Price = i.Price;
        l.Beds = i.Beds;
        l.Baths = i.Baths;
        l.PropertyType = i.PropertyType;
        l.Attributes = (i.Attributes ?? []).ToDictionary(kv => kv.Key, kv => ToPrimitive(kv.Value));
        l.Description = i.Description;
        l.Photos = i.Photos ?? [];
        l.TimeZone = i.TimeZone;
        l.Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(new GeoJson2DGeographicCoordinates(i.Location!.Lng, i.Location.Lat));
        if (i.ShowingRules is not null) l.ShowingRules = Normalize(i.ShowingRules);
    }

    private static ShowingRules Normalize(ShowingRules r)
    {
        r.Weekly = r.Weekly
            .Select(w => new WeeklyWindow { DayOfWeek = w.Day.ToString(), Start = TimeOfDay.Format(w.StartTime), End = w.End == "24:00" ? "23:59" : TimeOfDay.Format(w.EndTime) })
            .OrderBy(w => (int)w.Day).ThenBy(w => w.Start).ToList();
        r.BlackoutDates = r.BlackoutDates.Distinct().Order().ToList();
        return r;
    }

    private static object? ToPrimitive(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        _ => null,
    };
}
