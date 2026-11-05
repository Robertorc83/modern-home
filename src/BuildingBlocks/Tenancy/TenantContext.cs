namespace HomeShowing.BuildingBlocks.Tenancy;

public static class Roles
{
    public const string BrokerageAdmin = "BrokerageAdmin";
    public const string Agent = "Agent";
    public const string Buyer = "Buyer";
}

/// <summary>
/// The caller's identity for the current scope. Populated from the JWT / personal access token
/// claims by middleware (never from the request body). Every tenant scoped repository reads
/// <see cref="TenantId"/> from here, so a handler cannot forget the tenant filter.
/// </summary>
public interface ITenantContext
{
    bool IsSet { get; }
    string TenantId { get; }
    string UserId { get; }
    string Role { get; }
    string? UserName { get; }
    bool IsInRole(string role);
}

public sealed class TenantContext : ITenantContext
{
    private string? _tenantId;
    private string? _userId;
    private string? _role;

    public bool IsSet => _tenantId is not null;
    public string TenantId => _tenantId ?? throw new InvalidOperationException("Tenant context is not set for this scope.");
    public string UserId => _userId ?? throw new InvalidOperationException("User is not set for this scope.");
    public string Role => _role ?? throw new InvalidOperationException("Role is not set for this scope.");
    public string? UserName { get; private set; }

    public bool IsInRole(string role) => string.Equals(_role, role, StringComparison.Ordinal);

    public void Set(string tenantId, string userId, string role, string? userName = null)
    {
        if (_tenantId is not null && _tenantId != tenantId)
            throw new InvalidOperationException("Tenant context cannot change within a scope.");
        _tenantId = tenantId;
        _userId = userId;
        _role = role;
        UserName = userName;
    }
}
