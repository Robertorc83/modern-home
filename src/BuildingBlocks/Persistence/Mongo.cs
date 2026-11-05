using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.BuildingBlocks.Persistence;

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true";
    public string Database { get; set; } = "showings";
}

/// <summary>Singleton owner of the client and database handle.</summary>
public sealed class MongoDb
{
    public MongoDb(MongoOptions options)
    {
        BsonSetup.EnsureRegistered();
        var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
        Client = new MongoClient(settings);
        Database = Client.GetDatabase(options.Database);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }
    public IMongoCollection<T> Collection<T>(string name) => Database.GetCollection<T>(name);
}

public static class BsonSetup
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        ConventionRegistry.Register("hss", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreIfNullConvention(true),
        }, _ => true);
        BsonSerializer.TryRegisterSerializer(new InstantSerializer());
        BsonSerializer.TryRegisterSerializer(new ObjectSerializer(ObjectSerializer.AllAllowedTypes));
    }
}

/// <summary>Stores NodaTime instants as BSON dates (UTC, millisecond precision).</summary>
public sealed class InstantSerializer : SerializerBase<Instant>
{
    public override Instant Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        Instant.FromUnixTimeMilliseconds(context.Reader.ReadDateTime());

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Instant value) =>
        context.Writer.WriteDateTime(value.ToUnixTimeMilliseconds());
}

/// <summary>
/// Holds the session for the current DI scope. Repositories enlist in it automatically, so a
/// handler and the domain event handlers it triggers (e.g. the outbox writer) share one transaction.
/// </summary>
public sealed class MongoSessionScope
{
    public IClientSessionHandle? Current { get; internal set; }
    public bool InTransaction => Current is { IsInTransaction: true };
}

public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in a multi document transaction (retried by the driver on
    /// transient errors). Throwing <see cref="AbortTransactionException"/> rolls back without retry.</summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}

public sealed class AbortTransactionException(object payload) : Exception("Transaction aborted by the application.")
{
    public object Payload { get; } = payload;
}

public sealed class MongoUnitOfWork(MongoDb db, MongoSessionScope scope) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (scope.InTransaction) return await work(ct); // already enlisted

        using var session = await db.Client.StartSessionAsync(cancellationToken: ct);
        scope.Current = session;
        try
        {
            var options = new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority);
            return await session.WithTransactionAsync((_, token) => work(token), options, ct);
        }
        finally
        {
            scope.Current = null;
        }
    }
}

public static class MongoErrors
{
    public static bool IsDuplicateKey(Exception ex) => ex switch
    {
        MongoWriteException w => w.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        MongoCommandException c => c.Code == 11000,
        MongoBulkWriteException b => b.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey),
        _ => false,
    };
}
