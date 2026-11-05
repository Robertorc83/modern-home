using System.Text.Json;

namespace HomeShowing.Listings.Contracts;

public enum ListingStatus { Active, Draft, Off }
public enum PropertyType { House, Condo, Townhouse }

public sealed record GeoPoint(double Lat, double Lng);

public sealed class ListingDto
{
    public string Id { get; set; } = "";
    public string OwnerUserId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public ListingStatus Status { get; set; }
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string? Neighborhood { get; set; }
    public decimal Price { get; set; }
    public int Beds { get; set; }
    public decimal Baths { get; set; }
    public PropertyType PropertyType { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = [];
    public string? Description { get; set; }
    public List<string> Photos { get; set; } = [];
    public string TimeZone { get; set; } = "";
    public GeoPoint Location { get; set; } = new(0, 0);
    public ShowingRules ShowingRules { get; set; } = new();
    /// <summary>Incremented on every save; send it back in <see cref="ListingInput.Version"/> to detect lost updates.</summary>
    public int Version { get; set; }
}

/// <summary>Create / update payload. Owner and tenant come from the caller's token, never from here.</summary>
public sealed class ListingInput
{
    /// <summary>Defaults to Active on create; left unchanged on update when omitted.</summary>
    public ListingStatus? Status { get; set; }
    /// <summary>Optional on update: the version the client last read. A mismatch is a 409.</summary>
    public int? Version { get; set; }
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string? Neighborhood { get; set; }
    public decimal Price { get; set; }
    public int Beds { get; set; }
    public decimal Baths { get; set; }
    public PropertyType PropertyType { get; set; }
    public Dictionary<string, JsonElement>? Attributes { get; set; }
    public string? Description { get; set; }
    public List<string>? Photos { get; set; }
    public string TimeZone { get; set; } = "";
    public GeoPoint? Location { get; set; }
    public ShowingRules? ShowingRules { get; set; }
}

public sealed class ListingQuery
{
    public bool Mine { get; set; }
    public string? City { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MinBeds { get; set; }
    public PropertyType? PropertyType { get; set; }
    public bool ActiveOnly { get; set; }
    /// <summary>Place name ("Denver", "LoDo") resolved by the gazetteer, or explicit coordinates.</summary>
    public string? Near { get; set; }
    public double? NearLat { get; set; }
    public double? NearLng { get; set; }
    public double? RadiusKm { get; set; }
    public IReadOnlyCollection<string>? Ids { get; set; }
}

public sealed record ListingSearchHit(ListingDto Listing, double? DistanceKm);

/// <summary>Shared range checks for geo search input, so REST, MCP and matching all answer 400 instead of a Mongo error.</summary>
public static class GeoValidation
{
    public const double MaxRadiusKm = 500;

    /// <summary>Null when the query's geo parameters are usable; otherwise the validation message.</summary>
    public static string? Validate(ListingQuery q)
    {
        if (q.RadiusKm is { } r && (double.IsNaN(r) || r <= 0 || r > MaxRadiusKm)) return $"radiusKm must be greater than 0 and at most {MaxRadiusKm}.";
        if (q.NearLat is null != q.NearLng is null) return "nearLat and nearLng must be given together.";
        if (q.NearLat is { } lat && (double.IsNaN(lat) || lat is < -90 or > 90)) return "nearLat must be between -90 and 90.";
        if (q.NearLng is { } lng && (double.IsNaN(lng) || lng is < -180 or > 180)) return "nearLng must be between -180 and 180.";
        return null;
    }
}
