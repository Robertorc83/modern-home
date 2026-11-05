using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Scheduling.Domain;
using MongoDB.Bson;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.Scheduling.Infrastructure;

public sealed class ShowingRepository(MongoDb db, ITenantContext tenant, MongoSessionScope session)
    : TenantScopedRepository<Showing>(db, tenant, session, CollectionName)
{
    public const string CollectionName = "showings";

    public Task AddAsync(Showing s, CancellationToken ct) => InsertAsync(s, ct);

    /// <summary>Busy intervals per listing: every showing that still holds its slot.</summary>
    public async Task<ILookup<string, Interval>> ActiveByListingAsync(IReadOnlyCollection<string> listingIds, Instant from, CancellationToken ct)
    {
        var list = await FindAsync(F.And(F.In(x => x.ListingId, listingIds), F.Exists(x => x.ActiveSlotKey), F.Gte(x => x.SlotEndUtc, from)), ct: ct);
        return list.ToLookup(s => s.ListingId, s => new Interval(s.SlotStartUtc, s.SlotEndUtc));
    }

    public Task<List<Showing>> ForBuyerAsync(string buyerId, CancellationToken ct) =>
        FindAsync(F.Eq(x => x.BuyerId, buyerId), Builders<Showing>.Sort.Ascending(x => x.SlotStartUtc), 500, ct);

    public Task<List<Showing>> ForAgentAsync(string? agentId, ShowingStatus? status, CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Showing>>();
        if (agentId is not null) filters.Add(F.Eq(x => x.AgentId, agentId));
        if (status is not null) filters.Add(F.Eq(x => x.Status, status));
        return FindAsync(filters.Count > 0 ? F.And(filters) : null, Builders<Showing>.Sort.Ascending(x => x.SlotStartUtc), 500, ct);
    }

    public Task<List<Showing>> ForListingAsync(string listingId, Instant from, CancellationToken ct) =>
        FindAsync(F.And(F.Eq(x => x.ListingId, listingId), F.Gte(x => x.SlotEndUtc, from)), Builders<Showing>.Sort.Ascending(x => x.SlotStartUtc), 500, ct);

    /// <summary>Atomic, conditional transition: only succeeds if the showing is still in one of <paramref name="from"/>.
    /// Leaving an active state removes <c>activeSlotKey</c>, which frees the slot in the unique index.</summary>
    public Task<Showing?> TransitionAsync(string id, ShowingStatus[] from, ShowingStatus to, Instant now, CancellationToken ct)
    {
        var update = Builders<Showing>.Update.Set(x => x.Status, to).Set(x => x.UpdatedAt, now);
        if (to is ShowingStatus.Declined or ShowingStatus.Cancelled) update = update.Unset(x => x.ActiveSlotKey);
        return FindOneAndUpdateAsync(F.And(F.Eq(x => x.Id, id), F.In(x => x.Status, from)), update, ct)!;
    }
}

public sealed class SlotHoldRepository(MongoDb db, ITenantContext tenant, MongoSessionScope session)
    : TenantScopedRepository<SlotHold>(db, tenant, session, CollectionName)
{
    public const string CollectionName = "slotHolds";

    public Task AddAsync(SlotHold h, CancellationToken ct) => InsertAsync(h, ct);
    public Task<SlotHold?> ByKeyAsync(string key, CancellationToken ct) => FindOneAsync(F.Eq(x => x.ActiveSlotKey, key), ct);

    /// <summary>Live holds (TTL deletion runs about once a minute, so expiry is also checked in the query).</summary>
    public async Task<ILookup<string, Interval>> LiveByListingAsync(IReadOnlyCollection<string> listingIds, string? exceptBuyerId, Instant now, CancellationToken ct)
    {
        var filter = F.And(F.In(x => x.ListingId, listingIds), F.Gt(x => x.ExpiresAt, now));
        if (exceptBuyerId is not null) filter = F.And(filter, F.Ne(x => x.BuyerId, exceptBuyerId));
        var list = await FindAsync(filter, ct: ct);
        return list.ToLookup(h => h.ListingId, h => new Interval(h.SlotStartUtc, h.SlotEndUtc));
    }

    public async Task<bool> DeleteAsync(string id, string buyerId, CancellationToken ct) =>
        MongoDB.Bson.ObjectId.TryParse(id, out _) && (await DeleteOneAsync(F.And(F.Eq(x => x.Id, id), F.Eq(x => x.BuyerId, buyerId)), ct)).DeletedCount == 1;

    public Task DeleteExpiredAsync(string key, Instant now, CancellationToken ct) =>
        DeleteOneAsync(F.And(F.Eq(x => x.ActiveSlotKey, key), F.Lte(x => x.ExpiresAt, now)), ct);

    public Task DeleteByKeyForBuyerAsync(string key, string buyerId, CancellationToken ct) =>
        DeleteOneAsync(F.And(F.Eq(x => x.ActiveSlotKey, key), F.Eq(x => x.BuyerId, buyerId)), ct);

    public async Task ReleaseOthersAsync(string buyerId, string keepKey, CancellationToken ct)
    {
        // One hold per buyer: opening a new confirmation releases the previous slot for everyone else.
        await Collection.DeleteManyAsync(Scoped(F.And(F.Eq(x => x.BuyerId, buyerId), F.Ne(x => x.ActiveSlotKey, keepKey))), ct);
    }

    public Task<SlotHold> ExtendAsync(string id, Instant expiresAt, CancellationToken ct) =>
        FindOneAndUpdateAsync(F.Eq(x => x.Id, id), Builders<SlotHold>.Update.Set(x => x.ExpiresAt, expiresAt), ct);
}

public sealed class SchedulingSchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        var showingSchema = BsonDocument.Parse("""
        {
          bsonType: "object",
          required: ["tenantId", "listingId", "buyerId", "agentId", "slotStartUtc", "slotEndUtc", "status", "createdAt"],
          properties: {
            tenantId: { bsonType: "string", minLength: 1 },
            listingId: { bsonType: "string", minLength: 1 },
            buyerId: { bsonType: "string", minLength: 1 },
            agentId: { bsonType: "string", minLength: 1 },
            slotStartUtc: { bsonType: "date" },
            slotEndUtc: { bsonType: "date" },
            status: { enum: ["Requested", "Confirmed", "Declined", "Cancelled"] },
            activeSlotKey: { bsonType: "string" }
          },
          oneOf: [
            { properties: { status: { enum: ["Requested", "Confirmed"] } }, required: ["activeSlotKey"] },
            { properties: { status: { enum: ["Declined", "Cancelled"] } }, not: { required: ["activeSlotKey"] } }
          ]
        }
        """);
        await MongoSchema.EnsureCollectionAsync(db.Database, ShowingRepository.CollectionName, showingSchema, ct);
        var showings = db.Collection<Showing>(ShowingRepository.CollectionName);
        var k = Builders<Showing>.IndexKeys;
        await showings.Indexes.CreateManyAsync([
            new CreateIndexModel<Showing>(k.Ascending(x => x.TenantId).Ascending(x => x.ActiveSlotKey), new CreateIndexOptions<Showing>
            {
                Name = "ux_tenant_activeSlotKey",
                Unique = true,
                PartialFilterExpression = Builders<Showing>.Filter.Exists(x => x.ActiveSlotKey),
            }),
            new CreateIndexModel<Showing>(k.Ascending(x => x.TenantId).Ascending(x => x.AgentId).Ascending(x => x.SlotStartUtc)),
            new CreateIndexModel<Showing>(k.Ascending(x => x.TenantId).Ascending(x => x.BuyerId).Ascending(x => x.SlotStartUtc)),
            new CreateIndexModel<Showing>(k.Ascending(x => x.TenantId).Ascending(x => x.ListingId).Ascending(x => x.SlotStartUtc)),
        ], ct);

        await MongoSchema.EnsureCollectionAsync(db.Database, SlotHoldRepository.CollectionName, null, ct);
        var holds = db.Collection<SlotHold>(SlotHoldRepository.CollectionName);
        var hk = Builders<SlotHold>.IndexKeys;
        await holds.Indexes.CreateManyAsync([
            new CreateIndexModel<SlotHold>(hk.Ascending(x => x.ExpiresAt), new CreateIndexOptions { Name = "ttl_expiresAt", ExpireAfter = TimeSpan.Zero }),
            new CreateIndexModel<SlotHold>(hk.Ascending(x => x.TenantId).Ascending(x => x.ActiveSlotKey), new CreateIndexOptions { Name = "ux_tenant_activeSlotKey", Unique = true }),
            new CreateIndexModel<SlotHold>(hk.Ascending(x => x.TenantId).Ascending(x => x.ListingId)),
        ], ct);
    }
}
