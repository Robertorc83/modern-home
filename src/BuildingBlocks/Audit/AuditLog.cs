using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.BuildingBlocks.Audit;

public sealed class AuditEntry : TenantDocument
{
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public Instant At { get; set; }
}

public interface IAuditLog
{
    Task RecordAsync(string action, string entityId, CancellationToken ct);
}

public sealed class MongoAuditLog(MongoDb db, ITenantContext tenant, MongoSessionScope session, IClock clock)
    : TenantScopedRepository<AuditEntry>(db, tenant, session, "audit"), IAuditLog
{
    public Task RecordAsync(string action, string entityId, CancellationToken ct) =>
        InsertAsync(new AuditEntry { UserId = Tenant.UserId, Action = action, EntityId = entityId, At = clock.GetCurrentInstant() }, ct);
}
