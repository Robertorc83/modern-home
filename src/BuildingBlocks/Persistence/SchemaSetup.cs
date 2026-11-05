using MongoDB.Bson;
using MongoDB.Driver;

namespace HomeShowing.BuildingBlocks.Persistence;

/// <summary>Each module declares its own collections, validators and indexes. Run once at startup
/// (idempotent), so a fresh database is correct before the first request.</summary>
public interface IMongoSchemaSetup
{
    Task ApplyAsync(MongoDb db, CancellationToken ct);
}

public static class MongoSchema
{
    public static async Task EnsureCollectionAsync(IMongoDatabase db, string name, BsonDocument? jsonSchema, CancellationToken ct)
    {
        var exists = await (await db.ListCollectionNamesAsync(new ListCollectionNamesOptions { Filter = new BsonDocument("name", name) }, ct)).AnyAsync(ct);
        if (!exists)
        {
            var cmd = new BsonDocument("create", name);
            if (jsonSchema is not null) cmd.Add("validator", new BsonDocument("$jsonSchema", jsonSchema)).Add("validationLevel", "strict").Add("validationAction", "error");
            await db.RunCommandAsync<BsonDocument>(cmd, cancellationToken: ct);
        }
        else if (jsonSchema is not null)
        {
            await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", name }, { "validator", new BsonDocument("$jsonSchema", jsonSchema) }, { "validationLevel", "strict" } }, cancellationToken: ct);
        }
    }
}
