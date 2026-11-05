using HomeShowing.BuildingBlocks.Persistence;
using NodaTime;

namespace HomeShowing.Identity.Domain;

public sealed class User : TenantDocument
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "America/Denver";
    public Instant CreatedAt { get; set; }
}

/// <summary>A personal access token for MCP clients. Only a SHA-256 hash is stored; the raw value is shown once.</summary>
public sealed class PersonalAccessToken : TenantDocument
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Hash { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public Instant? LastUsedAt { get; set; }
    public Instant? RevokedAt { get; set; }
}
