using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Listings.Contracts;
using HomeShowing.Listings.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace HomeShowing.Listings.Infrastructure;

internal sealed class ListingRepository(MongoDb db, ITenantContext tenant, MongoSessionScope session)
    : TenantScopedRepository<Listing>(db, tenant, session, CollectionName)
{
    public const string CollectionName = "listings";

    public Task AddAsync(Listing l, CancellationToken ct) => InsertAsync(l, ct);
    public Task<bool> SaveAsync(Listing l, CancellationToken ct) => ReplaceAsync(l, ct);

    public async Task<List<(Listing Listing, double? DistanceKm)>> SearchAsync(ListingQuery q, string? ownerId, GeoPoint? near, CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Listing>>();
        if (ownerId is not null) filters.Add(F.Eq(x => x.OwnerUserId, ownerId));
        if (q.ActiveOnly) filters.Add(F.Eq(x => x.Status, ListingStatus.Active));
        if (!string.IsNullOrWhiteSpace(q.City)) filters.Add(F.Regex(x => x.City, new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(q.City.Trim())}$", "i")));
        if (q.MaxPrice is { } max) filters.Add(F.Lte(x => x.Price, max));
        if (q.MinBeds is { } beds) filters.Add(F.Gte(x => x.Beds, beds));
        if (q.PropertyType is { } pt) filters.Add(F.Eq(x => x.PropertyType, pt));
        if (q.Ids is { Count: > 0 } ids) filters.Add(F.In(x => x.Id, ids));
        var filter = filters.Count > 0 ? F.And(filters) : F.Empty;

        if (near is null)
        {
            var list = await FindAsync(filter, Builders<Listing>.Sort.Ascending(x => x.City).Ascending(x => x.Price), 200, ct);
            return list.Select(l => (l, (double?)null)).ToList();
        }

        // $geoNear must be the first stage; the tenant filter goes into its `query`, so it uses the
        // 2dsphere index and still never crosses tenants.
        var serializer = BsonSerializer.LookupSerializer<Listing>();
        var query = Scoped(filter).Render(new RenderArgs<Listing>(serializer, BsonSerializer.SerializerRegistry));
        var geoNear = new BsonDocument("$geoNear", new BsonDocument
        {
            { "near", new BsonDocument { { "type", "Point" }, { "coordinates", new BsonArray { near.Lng, near.Lat } } } },
            { "distanceField", "distanceMeters" },
            { "spherical", true },
            { "maxDistance", (q.RadiusKm ?? 25) * 1000 },
            { "query", query },
        });
        var docs = await Collection.Aggregate<BsonDocument>(new[] { geoNear, new BsonDocument("$limit", 200) }, cancellationToken: ct).ToListAsync(ct);
        return docs.Select(d =>
        {
            var meters = d["distanceMeters"].ToDouble();
            d.Remove("distanceMeters");
            return (BsonSerializer.Deserialize<Listing>(d), (double?)Math.Round(meters / 1000, 1));
        }).ToList();
    }
}

internal sealed class ListingSchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        // Flexible on purpose, not loose: the core fields are typed and required, while
        // `attributes` stays an open subdocument for per property type data.
        var schema = BsonDocument.Parse("""
        {
          bsonType: "object",
          required: ["tenantId", "ownerUserId", "status", "address", "city", "price", "beds", "propertyType", "timeZone", "location", "showingRules"],
          properties: {
            tenantId: { bsonType: "string", minLength: 1 },
            ownerUserId: { bsonType: "string", minLength: 1 },
            status: { enum: ["Active", "Draft", "Off"] },
            address: { bsonType: "string", minLength: 1 },
            city: { bsonType: "string", minLength: 1 },
            price: { bsonType: ["decimal", "double", "int", "long"], minimum: 0 },
            beds: { bsonType: "int", minimum: 0 },
            propertyType: { enum: ["House", "Condo", "Townhouse"] },
            attributes: { bsonType: "object" },
            timeZone: { bsonType: "string", minLength: 3 },
            location: {
              bsonType: "object", required: ["type", "coordinates"],
              properties: { type: { enum: ["Point"] }, coordinates: { bsonType: "array", minItems: 2, maxItems: 2 } }
            },
            showingRules: {
              bsonType: "object", required: ["slotMinutes", "weekly"],
              properties: {
                slotMinutes: { bsonType: "int", minimum: 10, maximum: 240 },
                bufferMinutes: { bsonType: "int", minimum: 0 },
                weekly: { bsonType: "array" },
                blackoutDates: { bsonType: "array", items: { bsonType: "string" } }
              }
            }
          }
        }
        """);
        await MongoSchema.EnsureCollectionAsync(db.Database, ListingRepository.CollectionName, schema, ct);
        var col = db.Collection<Listing>(ListingRepository.CollectionName);
        var k = Builders<Listing>.IndexKeys;
        await col.Indexes.CreateManyAsync([
            new CreateIndexModel<Listing>(k.Geo2DSphere(x => x.Location)),
            new CreateIndexModel<Listing>(k.Ascending(x => x.TenantId).Ascending(x => x.OwnerUserId)),
            new CreateIndexModel<Listing>(k.Ascending(x => x.TenantId).Ascending(x => x.Status).Ascending(x => x.City).Ascending(x => x.Price)),
        ], ct);
    }
}
