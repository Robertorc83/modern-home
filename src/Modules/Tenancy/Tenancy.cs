using HomeShowing.BuildingBlocks.Persistence;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;

namespace HomeShowing.Tenancy;

/// <summary>A tenant is a real estate brokerage. Everything else in the system carries its id.</summary>
public sealed class Tenant
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultTimeZone { get; set; } = "America/Denver";
}

public sealed record TenantInfo(string Id, string Slug, string Name, string DefaultTimeZone);

public interface ITenantDirectory
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct);
    Task<TenantInfo?> FindByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct);
    Task<TenantInfo> CreateAsync(string slug, string name, string defaultTimeZone, CancellationToken ct);
}

internal sealed class TenantDirectory(MongoDb db) : ITenantDirectory
{
    public const string CollectionName = "tenants";
    private IMongoCollection<Tenant> Col => db.Collection<Tenant>(CollectionName);
    private static TenantInfo Map(Tenant t) => new(t.Id, t.Slug, t.Name, t.DefaultTimeZone);

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct) =>
        await Col.Find(t => t.Slug == slug.ToLowerInvariant()).FirstOrDefaultAsync(ct) is { } t ? Map(t) : null;

    public async Task<TenantInfo?> FindByIdAsync(string id, CancellationToken ct) =>
        ObjectId.TryParse(id, out _) && await Col.Find(t => t.Id == id).FirstOrDefaultAsync(ct) is { } t ? Map(t) : null;

    public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct) =>
        (await Col.Find(FilterDefinition<Tenant>.Empty).SortBy(t => t.Name).ToListAsync(ct)).Select(Map).ToList();

    public async Task<TenantInfo> CreateAsync(string slug, string name, string defaultTimeZone, CancellationToken ct)
    {
        var t = new Tenant { Slug = slug.ToLowerInvariant(), Name = name, DefaultTimeZone = defaultTimeZone };
        await Col.InsertOneAsync(t, cancellationToken: ct);
        return Map(t);
    }
}

internal sealed class TenancySchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        await MongoSchema.EnsureCollectionAsync(db.Database, TenantDirectory.CollectionName, null, ct);
        await db.Collection<Tenant>(TenantDirectory.CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<Tenant>(Builders<Tenant>.IndexKeys.Ascending(t => t.Slug), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
    }
}

public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddSingleton<ITenantDirectory, TenantDirectory>();
        services.AddSingleton<IMongoSchemaSetup, TenancySchema>();
        return services;
    }
}
