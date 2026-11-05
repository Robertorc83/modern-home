using HomeShowing.BuildingBlocks.Persistence;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.BuildingBlocks.Outbox;

public sealed class OutboxMessage
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string TenantId { get; set; } = "";
    public string Type { get; set; } = "";
    public BsonDocument Payload { get; set; } = new();
    public Instant OccurredAt { get; set; }
    public Instant? ProcessedAt { get; set; }
    public Instant? LockedUntil { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public interface IOutbox
{
    /// <summary>Adds a message in the caller's transaction (if any). Never publishes directly.</summary>
    Task EnqueueAsync(string tenantId, string type, object payload, CancellationToken ct);
}

public sealed class MongoOutbox(MongoDb db, MongoSessionScope session, IClock clock) : IOutbox
{
    public const string CollectionName = "outbox";

    public Task EnqueueAsync(string tenantId, string type, object payload, CancellationToken ct)
    {
        var msg = new OutboxMessage { TenantId = tenantId, Type = type, Payload = payload.ToBsonDocument(), OccurredAt = clock.GetCurrentInstant() };
        var col = db.Collection<OutboxMessage>(CollectionName);
        return session.InTransaction ? col.InsertOneAsync(session.Current!, msg, cancellationToken: ct) : col.InsertOneAsync(msg, cancellationToken: ct);
    }
}

public sealed class OutboxSchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        await MongoSchema.EnsureCollectionAsync(db.Database, CollectionName, null, ct);
        var col = db.Collection<OutboxMessage>(CollectionName);
        await col.Indexes.CreateOneAsync(new CreateIndexModel<OutboxMessage>(
            Builders<OutboxMessage>.IndexKeys.Ascending(x => x.ProcessedAt).Ascending(x => x.OccurredAt)), cancellationToken: ct);
    }

    private const string CollectionName = MongoOutbox.CollectionName;
}
