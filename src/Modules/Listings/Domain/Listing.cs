using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Listings.Contracts;
using MongoDB.Driver.GeoJsonObjectModel;
using NodaTime;

namespace HomeShowing.Listings.Domain;

/// <summary>A listing is a natural document: per type attributes and the showing rules travel with it.</summary>
internal sealed class Listing : TenantDocument
{
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
    public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; set; } = null!;
    public ShowingRules ShowingRules { get; set; } = new();
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
    /// <summary>Optimistic concurrency stamp; documents written before it existed read as 0.</summary>
    public int Version { get; set; }

    public ListingDto ToDto() => new()
    {
        Id = Id, OwnerUserId = OwnerUserId, OwnerName = OwnerName, Status = Status, Address = Address, City = City,
        Neighborhood = Neighborhood, Price = Price, Beds = Beds, Baths = Baths, PropertyType = PropertyType,
        Attributes = Attributes, Description = Description, Photos = Photos, TimeZone = TimeZone,
        Location = new GeoPoint(Location.Coordinates.Latitude, Location.Coordinates.Longitude), ShowingRules = ShowingRules,
        Version = Version,
    };
}
