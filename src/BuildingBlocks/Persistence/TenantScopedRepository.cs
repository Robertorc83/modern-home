using HomeShowing.BuildingBlocks.Tenancy;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HomeShowing.BuildingBlocks.Persistence;

public interface ITenantDocument
{
    string Id { get; set; }
    string TenantId { get; set; }
}

public abstract class TenantDocument : ITenantDocument
{
    [BsonId, BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string TenantId { get; set; } = "";
}

/// <summary>
/// Base for every repository over a shared, multi tenant collection. All reads and writes go through
/// <see cref="Scoped"/>, which ANDs <c>tenantId</c> from the authenticated context into the filter.
/// A document id from another tenant simply does not match, so callers get "not found" (HTTP 404),
/// never a 403 that would confirm the document exists.
/// </summary>
public abstract class TenantScopedRepository<T>(MongoDb db, ITenantContext tenant, MongoSessionScope session, string collectionName)
    where T : ITenantDocument
{
    protected IMongoCollection<T> Collection { get; } = db.Collection<T>(collectionName);
    protected ITenantContext Tenant => tenant;
    protected static FilterDefinitionBuilder<T> F => Builders<T>.Filter;

    protected FilterDefinition<T> Scoped(FilterDefinition<T>? filter = null)
    {
        var byTenant = F.Eq(x => x.TenantId, tenant.TenantId);
        return filter is null ? byTenant : F.And(byTenant, filter);
    }

    private IClientSessionHandle? S => session.InTransaction ? session.Current : null;

    public Task<T?> GetByIdAsync(string id, CancellationToken ct = default) =>
        MongoDB.Bson.ObjectId.TryParse(id, out _) ? FindOneAsync(F.Eq(x => x.Id, id), ct) : Task.FromResult<T?>(default);

    protected async Task<T?> FindOneAsync(FilterDefinition<T> filter, CancellationToken ct)
    {
        var cursor = S is { } s ? Collection.Find(s, Scoped(filter)) : Collection.Find(Scoped(filter));
        return await cursor.Limit(1).FirstOrDefaultAsync(ct);
    }

    protected Task<List<T>> FindAsync(FilterDefinition<T>? filter, SortDefinition<T>? sort = null, int? limit = null, CancellationToken ct = default)
    {
        var find = S is { } s ? Collection.Find(s, Scoped(filter)) : Collection.Find(Scoped(filter));
        if (sort is not null) find = find.Sort(sort);
        if (limit is not null) find = find.Limit(limit);
        return find.ToListAsync(ct);
    }

    protected Task InsertAsync(T doc, CancellationToken ct)
    {
        doc.TenantId = tenant.TenantId; // never trust a tenant id from input
        return S is { } s ? Collection.InsertOneAsync(s, doc, cancellationToken: ct) : Collection.InsertOneAsync(doc, cancellationToken: ct);
    }

    protected async Task<bool> ReplaceAsync(T doc, CancellationToken ct)
    {
        doc.TenantId = tenant.TenantId;
        var filter = Scoped(F.Eq(x => x.Id, doc.Id));
        var r = S is { } s ? await Collection.ReplaceOneAsync(s, filter, doc, cancellationToken: ct) : await Collection.ReplaceOneAsync(filter, doc, cancellationToken: ct);
        return r.MatchedCount == 1;
    }

    protected Task<UpdateResult> UpdateOneAsync(FilterDefinition<T> filter, UpdateDefinition<T> update, CancellationToken ct) =>
        S is { } s ? Collection.UpdateOneAsync(s, Scoped(filter), update, cancellationToken: ct) : Collection.UpdateOneAsync(Scoped(filter), update, cancellationToken: ct);

    protected Task<T> FindOneAndUpdateAsync(FilterDefinition<T> filter, UpdateDefinition<T> update, CancellationToken ct)
    {
        var opts = new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After };
        return S is { } s ? Collection.FindOneAndUpdateAsync(s, Scoped(filter), update, opts, ct) : Collection.FindOneAndUpdateAsync(Scoped(filter), update, opts, ct);
    }

    protected Task<DeleteResult> DeleteOneAsync(FilterDefinition<T> filter, CancellationToken ct) =>
        S is { } s ? Collection.DeleteOneAsync(s, Scoped(filter), cancellationToken: ct) : Collection.DeleteOneAsync(Scoped(filter), ct);
}
