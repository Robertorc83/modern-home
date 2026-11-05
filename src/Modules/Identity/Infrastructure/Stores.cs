using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Domain;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.Identity.Infrastructure;

/// <summary>Tenant scoped user queries (admin screens, directory lookups).</summary>
public sealed class UserRepository(MongoDb db, ITenantContext tenant, MongoSessionScope session)
    : TenantScopedRepository<User>(db, tenant, session, CollectionName)
{
    public const string CollectionName = "users";

    public Task AddAsync(User u, CancellationToken ct) => InsertAsync(u, ct);
    public Task<List<User>> ByRoleAsync(string role, CancellationToken ct) =>
        FindAsync(F.Eq(x => x.Role, role), Builders<User>.Sort.Ascending(x => x.Name), 500, ct);
}

/// <summary>
/// The only cross tenant reads in the system, and both happen before a tenant is known:
/// sign in by email, and resolving a personal access token by its hash.
/// </summary>
public sealed class CredentialStore(MongoDb db)
{
    private IMongoCollection<User> Users => db.Collection<User>(UserRepository.CollectionName);
    private IMongoCollection<PersonalAccessToken> Tokens => db.Collection<PersonalAccessToken>(TokenRepository.CollectionName);

    public Task<User> FindForLoginAsync(string email, CancellationToken ct) =>
        Users.Find(u => u.Email == email.Trim().ToLowerInvariant()).FirstOrDefaultAsync(ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        Users.Find(u => u.Email == email.Trim().ToLowerInvariant()).AnyAsync(ct);

    public Task InsertUserAsync(User u, CancellationToken ct) => Users.InsertOneAsync(u, cancellationToken: ct);

    public Task<User> FindUserAsync(string tenantId, string userId, CancellationToken ct) =>
        Users.Find(u => u.TenantId == tenantId && u.Id == userId).FirstOrDefaultAsync(ct);

    public Task<PersonalAccessToken> FindTokenByHashAsync(string hash, CancellationToken ct) =>
        Tokens.Find(t => t.Hash == hash).FirstOrDefaultAsync(ct);

    public Task TouchTokenAsync(string id, Instant now, CancellationToken ct) =>
        Tokens.UpdateOneAsync(t => t.Id == id, Builders<PersonalAccessToken>.Update.Set(t => t.LastUsedAt, now), cancellationToken: ct);
}

public sealed class TokenRepository(MongoDb db, ITenantContext tenant, MongoSessionScope session)
    : TenantScopedRepository<PersonalAccessToken>(db, tenant, session, CollectionName)
{
    public const string CollectionName = "personalAccessTokens";

    public Task AddAsync(PersonalAccessToken t, CancellationToken ct) => InsertAsync(t, ct);
    public Task<List<PersonalAccessToken>> ForUserAsync(string userId, CancellationToken ct) =>
        FindAsync(F.Eq(x => x.UserId, userId), Builders<PersonalAccessToken>.Sort.Descending(x => x.CreatedAt), 100, ct);
    public async Task<bool> RevokeAsync(string id, string userId, Instant now, CancellationToken ct) =>
        MongoDB.Bson.ObjectId.TryParse(id, out _) &&
        (await UpdateOneAsync(F.And(F.Eq(x => x.Id, id), F.Eq(x => x.UserId, userId), F.Eq(x => x.RevokedAt, null)),
            Builders<PersonalAccessToken>.Update.Set(x => x.RevokedAt, now), ct)).ModifiedCount == 1;
}

internal sealed class IdentitySchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        await MongoSchema.EnsureCollectionAsync(db.Database, UserRepository.CollectionName, null, ct);
        await MongoSchema.EnsureCollectionAsync(db.Database, TokenRepository.CollectionName, null, ct);
        var users = db.Collection<User>(UserRepository.CollectionName);
        await users.Indexes.CreateManyAsync([
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.TenantId).Ascending(u => u.Role)),
        ], ct);
        var tokens = db.Collection<PersonalAccessToken>(TokenRepository.CollectionName);
        await tokens.Indexes.CreateManyAsync([
            new CreateIndexModel<PersonalAccessToken>(Builders<PersonalAccessToken>.IndexKeys.Ascending(t => t.Hash), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<PersonalAccessToken>(Builders<PersonalAccessToken>.IndexKeys.Ascending(t => t.TenantId).Ascending(t => t.UserId)),
        ], ct);
    }
}
