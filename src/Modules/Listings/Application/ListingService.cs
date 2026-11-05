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
    /// <summary>Role aware, validated search (the REST and MCP list/search entry point).</summary>
    Task<Result<IReadOnlyList<ListingSearchHit>>> ListAsync(ListingQuery query, CancellationToken ct);
    /// <summary>Role aware read: a listing the caller is not allowed to see is not found.</summary>
    Task<Result<ListingDto>> GetAsync(string id, CancellationToken ct);
    /// <summary>Listing count per owner user id in the caller's tenant.</summary>
    Task<Dictionary<string, int>> CountByOwnerAsync(CancellationToken ct);
}

public sealed class ListingService : IListingCatalog
{
    private readonly ListingRepository _repo;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IPhotoStorage _photos;

    internal ListingService(ListingRepository repo, ITenantContext tenant, IClock clock, IAuditLog audit, IPhotoStorage photos)
    {
        _repo = repo; _tenant = tenant; _clock = clock; _audit = audit; _photos = photos;
    }

    /// <summary>Listing as the caller sees it: stored S3 keys become short lived presigned GET URLs.</summary>
    private ListingDto Dto(Listing l)
    {
        var dto = l.ToDto();
        dto.Photos = l.Photos.Select(_photos.ToDisplayUrl).ToList();
        return dto;
    }

    public async Task<ListingDto?> FindAsync(string id, CancellationToken ct) => await _repo.GetByIdAsync(id, ct) is { } l ? Dto(l) : null;

    public async Task<Result<PhotoUpload>> CreatePhotoUploadAsync(string id, string contentType, CancellationToken ct)
    {
        if (!_photos.Enabled) return Error.Unprocessable("Photo uploads are off", "Start the stack with AWS (LocalStack) enabled, or paste an image URL.");
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null) return Error.NotFound("Listing");
        if (CanEdit(l) is { } denied) return denied;
        try { return _photos.CreateUpload(_tenant.TenantId, l.Id, contentType); }
        catch (ArgumentException ex) { return Error.Validation(ex.Message); }
    }

    public async Task<IReadOnlyList<ListingSearchHit>> SearchAsync(ListingQuery query, CancellationToken ct)
    {
        var hits = await _repo.SearchAsync(query, null, ResolveNear(query), ct);
        return hits.Select(h => new ListingSearchHit(Dto(h.Listing), h.DistanceKm)).ToList();
    }

    private static GeoPoint? ResolveNear(ListingQuery query)
    {
        if (query.NearLat is { } lat && query.NearLng is { } lng) return new GeoPoint(lat, lng);
        return Places.TryResolve(query.Near, out var p) ? p : null;
    }

    public Task<Dictionary<string, int>> CountByOwnerAsync(CancellationToken ct) => _repo.CountByOwnerAsync(ct);

    /// <summary>Role aware listing: buyers see active listings, agents see active ones plus their own (or only their own
    /// with <c>mine</c>), admins see all.</summary>
    public async Task<Result<IReadOnlyList<ListingSearchHit>>> ListAsync(ListingQuery query, CancellationToken ct)
    {
        if (GeoValidation.Validate(query) is { } geo) return Error.Validation(geo);
        if (!string.IsNullOrWhiteSpace(query.Near) && query.NearLat is null && !Places.TryResolve(query.Near, out _))
            return Error.Validation($"Unknown place '{query.Near}'. Try one of: {string.Join(", ", Places.Names.Take(8))}.");
        if (_tenant.IsInRole(Roles.Buyer)) query.ActiveOnly = true;
        var agent = _tenant.IsInRole(Roles.Agent);
        var hits = await _repo.SearchAsync(query, query.Mine && agent ? _tenant.UserId : null, ResolveNear(query), ct,
            activeOrOwnedBy: agent ? _tenant.UserId : null);
        return hits.Select(h => new ListingSearchHit(Dto(h.Listing), h.DistanceKm)).ToList();
    }

    public async Task<Result<ListingDto>> GetAsync(string id, CancellationToken ct)
    {
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null || !CanSee(l)) return Error.NotFound("Listing");
        return Dto(l);
    }

    /// <summary>Draft and Off listings are visible to their owner and brokerage admins only.</summary>
    private bool CanSee(Listing l) =>
        l.Status == ListingStatus.Active || _tenant.IsInRole(Roles.BrokerageAdmin) || (_tenant.IsInRole(Roles.Agent) && l.OwnerUserId == _tenant.UserId);

    public async Task<Result<ListingDto>> CreateAsync(ListingInput input, CancellationToken ct)
    {
        if (!_tenant.IsInRole(Roles.Agent)) return Error.Forbidden("Only agents create listings.");
        if ((Validate(input) ?? ValidatePhotos(input, null)) is { } err) return err;
        var now = _clock.GetCurrentInstant();
        var l = new Listing { OwnerUserId = _tenant.UserId, OwnerName = _tenant.UserName ?? "", CreatedAt = now, UpdatedAt = now, Status = ListingStatus.Active };
        Apply(l, input);
        await _repo.AddAsync(l, ct);
        await _audit.RecordAsync("listing.created", l.Id, ct);
        return Dto(l);
    }

    private static readonly Error StaleListing = Error.Conflict("Listing changed, reload and try again.");

    public async Task<Result<ListingDto>> UpdateAsync(string id, ListingInput input, CancellationToken ct)
    {
        var l = await _repo.GetByIdAsync(id, ct);
        if (l is null) return Error.NotFound("Listing");
        if (CanEdit(l) is { } denied) return denied;
        if (input.Version is { } v && v != l.Version) return StaleListing;
        if ((Validate(input) ?? ValidatePhotos(input, l.Id)) is { } err) return err;
        var rules = l.ShowingRules;
        Apply(l, input);
        if (input.ShowingRules is null) l.ShowingRules = rules;
        l.UpdatedAt = _clock.GetCurrentInstant();
        if (!await _repo.SaveAsync(l, ct)) return StaleListing;
        await _audit.RecordAsync("listing.updated", l.Id, ct);
        return Dto(l);
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
        if (!await _repo.SaveAsync(l, ct)) return StaleListing;
        await _audit.RecordAsync("listing.rules_updated", l.Id, ct);
        return Dto(l);
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

    /// <summary>Photos are external https URLs or keys this listing uploaded to our bucket (never another listing's).</summary>
    private Error? ValidatePhotos(ListingInput i, string? listingId)
    {
        i.Photos = (i.Photos ?? []).Select(p => _photos.ToStoredValue(p.Trim())).Where(p => p.Length > 0).Distinct().ToList();
        if (i.Photos.Count > 12) return Error.Validation("At most 12 photos.");
        foreach (var p in i.Photos)
        {
            if (p.StartsWith(S3PhotoStorage.KeyPrefix, StringComparison.Ordinal))
            {
                if (listingId is null || !_photos.IsOwnKey(_tenant.TenantId, listingId, p)) return Error.Validation("A photo key does not belong to this listing.");
            }
            else if (!Uri.TryCreate(p, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                return Error.Validation($"Photo '{p}' must be an https URL.");
        }
        return null;
    }

    private static void Apply(Listing l, ListingInput i)
    {
        if (i.Status is { } status) l.Status = status;
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
            .Select(w => new WeeklyWindow { DayOfWeek = w.Day.ToString(), Start = TimeOfDay.Format(w.StartTime), End = w.End == "24:00" ? "24:00" : TimeOfDay.Format(w.EndTime) })
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
