using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HomeShowing.BuildingBlocks.Results;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Domain;
using HomeShowing.Identity.Infrastructure;
using HomeShowing.Listings.Application;
using HomeShowing.Tenancy;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace HomeShowing.Identity.Application;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "home-showing-scheduler";
    public string Audience { get; set; } = "home-showing-scheduler";
    /// <summary>The checked in development key. Refused outside Development unless Auth:AllowDevKey is true.</summary>
    public const string DevSigningKey = "dev-only-signing-key-change-me-0123456789abcdef";
    /// <summary>HMAC key, at least 32 bytes. Overridden by Auth__SigningKey in every real environment.</summary>
    public string SigningKey { get; set; } = DevSigningKey;
    public int LifetimeHours { get; set; } = 12;
    public bool IsDevKey => SigningKey == DevSigningKey;
}

public static class AppClaims
{
    public const string TenantId = "tenantId";
    public const string Role = "role";
    public const string Name = "name";
    public const string Email = "email";
    public const string TimeZone = "tz";
    public const string Sub = "sub";
}

public sealed record UserView(string Id, string Email, string Name, string Role, string TenantId, string TenantSlug, string TenantName, string TimeZone);
public sealed record AuthResponse(string Token, UserView User);
/// <param name="TenantSlug">Optional: which brokerage to sign in to when the same email exists in more than one.</param>
public sealed record LoginRequest(string Email, string Password, string? TenantSlug = null);
/// <summary>A brokerage choice returned in the 409 <c>tenants</c> extension when a login is ambiguous.</summary>
public sealed record TenantChoice(string Slug, string Name);
public sealed record RegisterBuyerRequest(string TenantSlug, string Email, string Password, string Name, string? TimeZone = null);
public sealed record CreateAgentRequest(string Name, string Email, string Password);
public sealed record AgentView(string Id, string Name, string Email, string TimeZone, int ListingCount = 0);
public sealed record TokenView(string Id, string Name, string Prefix, Instant CreatedAt, Instant? LastUsedAt, Instant? RevokedAt);
public sealed record CreatedToken(string Id, string Name, string Token);
public sealed record CreateTokenRequest(string Name);

public sealed class JwtIssuer(JwtOptions options, IClock clock)
{
    public SymmetricSecurityKey Key { get; } = new(Encoding.UTF8.GetBytes(options.SigningKey));

    public string Issue(User u)
    {
        var now = clock.GetCurrentInstant().ToDateTimeUtc();
        var claims = new[]
        {
            new Claim(AppClaims.Sub, u.Id), new Claim(AppClaims.TenantId, u.TenantId), new Claim(AppClaims.Role, u.Role),
            new Claim(AppClaims.Name, u.Name), new Claim(AppClaims.Email, u.Email), new Claim(AppClaims.TimeZone, u.TimeZone),
        };
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, now, now.AddHours(options.LifetimeHours),
            new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class PatFormat
{
    public const string Prefix = "hss_pat_";
    public static string NewToken() => Prefix + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class AuthService(
    CredentialStore credentials, UserRepository users, TokenRepository tokens, ITenantDirectory tenants,
    IListingCatalog listings, JwtIssuer jwt, IClock clock, ITenantContext tenant)
{
    // A fixed hash to compare against when the email is unknown, so timing does not reveal which emails exist.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password", 11);

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        // Emails are unique per brokerage, so the same address may exist in several tenants (with their own passwords).
        // Login has no tenant yet: verify the password against every candidate (optionally narrowed to tenantSlug).
        // Exactly one match signs in; more than one is ambiguous and the client must pass tenantSlug.
        var candidates = await credentials.FindForLoginAsync(req.Email ?? "", ct);
        if (!string.IsNullOrWhiteSpace(req.TenantSlug))
        {
            var chosen = await tenants.FindBySlugAsync(req.TenantSlug.Trim(), ct);
            candidates = chosen is null ? [] : candidates.Where(c => c.TenantId == chosen.Id).ToList();
        }
        var password = req.Password ?? "";
        var matches = candidates.Where(c => BCrypt.Net.BCrypt.Verify(password, c.PasswordHash)).ToList();
        if (candidates.Count == 0) BCrypt.Net.BCrypt.Verify(password, DummyHash); // keep unknown emails as slow as known ones
        if (matches.Count == 0) return Error.Unauthorized("Email or password is incorrect.");
        if (matches.Count > 1)
        {
            var choices = new List<TenantChoice>();
            foreach (var m in matches)
                if (await tenants.FindByIdAsync(m.TenantId, ct) is { } t) choices.Add(new TenantChoice(t.Slug, t.Name));
            return Error.Conflict("That email exists in more than one brokerage; pass tenantSlug.", "Sign in again with the tenantSlug of the brokerage you want.",
                new Dictionary<string, object?> { ["tenants"] = choices.OrderBy(c => c.Name).ToList() });
        }
        return await RespondAsync(matches[0], ct);
    }

    public async Task<Result<AuthResponse>> RegisterBuyerAsync(RegisterBuyerRequest req, CancellationToken ct)
    {
        var t = await tenants.FindBySlugAsync(req.TenantSlug ?? "", ct);
        if (t is null) return Error.NotFound("Brokerage");
        if (string.IsNullOrWhiteSpace(req.Name)) return Error.Validation("name is required.");
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) return Error.Validation("A valid email is required.");
        if ((req.Password ?? "").Length < 8) return Error.Validation("Password must be at least 8 characters.");
        var zone = req.TimeZone is not null && DateTimeZoneProviders.Tzdb.GetZoneOrNull(req.TimeZone) is not null ? req.TimeZone : t.DefaultTimeZone;
        var user = NewUser(t.Id, req.Email, req.Password!, Roles.Buyer, req.Name, zone);
        try { await credentials.InsertUserAsync(user, ct); }
        catch (Exception ex) when (BuildingBlocks.Persistence.MongoErrors.IsDuplicateKey(ex)) { return Error.Conflict("An account with that email already exists."); }
        return await RespondAsync(user, ct);
    }

    public async Task<Result<UserView>> MeAsync(CancellationToken ct)
    {
        var u = await credentials.FindUserAsync(tenant.TenantId, tenant.UserId, ct);
        return u is null ? Error.Unauthorized("User no longer exists.") : await ViewAsync(u, ct);
    }

    public async Task<IReadOnlyList<AgentView>> ListAgentsAsync(CancellationToken ct)
    {
        var agents = await users.ByRoleAsync(Roles.Agent, ct);
        var counts = await listings.CountByOwnerAsync(ct);
        return agents.Select(u => new AgentView(u.Id, u.Name, u.Email, u.TimeZone, counts.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<Result<AgentView>> CreateAgentAsync(CreateAgentRequest req, CancellationToken ct)
    {
        if (!tenant.IsInRole(Roles.BrokerageAdmin)) return Error.Forbidden("Only brokerage admins add agents.");
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) return Error.Validation("name and a valid email are required.");
        if ((req.Password ?? "").Length < 8) return Error.Validation("Password must be at least 8 characters.");
        var t = await tenants.FindByIdAsync(tenant.TenantId, ct);
        var u = NewUser(tenant.TenantId, req.Email, req.Password!, Roles.Agent, req.Name, t?.DefaultTimeZone ?? "America/Denver");
        try { await users.AddAsync(u, ct); }
        catch (Exception ex) when (BuildingBlocks.Persistence.MongoErrors.IsDuplicateKey(ex)) { return Error.Conflict("An account with that email already exists."); }
        return new AgentView(u.Id, u.Name, u.Email, u.TimeZone);
    }

    public async Task<IReadOnlyList<TokenView>> ListTokensAsync(CancellationToken ct) =>
        (await tokens.ForUserAsync(tenant.UserId, ct)).Select(t => new TokenView(t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.RevokedAt)).ToList();

    public async Task<Result<CreatedToken>> CreateTokenAsync(CreateTokenRequest req, CancellationToken ct)
    {
        if (tenant.IsInRole(Roles.BrokerageAdmin)) return Error.Forbidden("Assistant tokens are for buyers and agents.");
        var raw = PatFormat.NewToken();
        var t = new PersonalAccessToken
        {
            UserId = tenant.UserId, Name = string.IsNullOrWhiteSpace(req.Name) ? "Claude" : req.Name.Trim(),
            Prefix = raw[..12], Hash = PatFormat.Hash(raw), CreatedAt = clock.GetCurrentInstant(),
        };
        await tokens.AddAsync(t, ct);
        return new CreatedToken(t.Id, t.Name, raw);
    }

    public async Task<Result<bool>> RevokeTokenAsync(string id, CancellationToken ct) =>
        await tokens.RevokeAsync(id, tenant.UserId, clock.GetCurrentInstant(), ct) ? true : Error.NotFound("Token");

    public User NewUser(string tenantId, string email, string password, string role, string name, string timeZone) => new()
    {
        TenantId = tenantId, Email = email.Trim().ToLowerInvariant(), PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 11),
        Role = role, Name = name.Trim(), TimeZone = timeZone, CreatedAt = clock.GetCurrentInstant(),
    };

    private async Task<AuthResponse> RespondAsync(User u, CancellationToken ct) => new(jwt.Issue(u), await ViewAsync(u, ct));

    private async Task<UserView> ViewAsync(User u, CancellationToken ct)
    {
        var t = await tenants.FindByIdAsync(u.TenantId, ct);
        return new UserView(u.Id, u.Email, u.Name, u.Role, u.TenantId, t?.Slug ?? "", t?.Name ?? "", u.TimeZone);
    }
}

/// <summary>Contact lookup for other modules (notifications).</summary>
public interface IUserDirectory
{
    Task<(string Name, string Email)?> FindContactAsync(string tenantId, string userId, CancellationToken ct);
}

internal sealed class UserDirectory(CredentialStore store) : IUserDirectory
{
    public async Task<(string Name, string Email)?> FindContactAsync(string tenantId, string userId, CancellationToken ct) =>
        await store.FindUserAsync(tenantId, userId, ct) is { } u ? (u.Name, u.Email) : null;
}
