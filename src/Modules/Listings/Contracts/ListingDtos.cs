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
}

/// <summary>Create / update payload. Owner and tenant come from the caller's token, never from here.</summary>
public sealed class ListingInput
{
    public ListingStatus Status { get; set; } = ListingStatus.Active;
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
