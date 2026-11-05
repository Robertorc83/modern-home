using System.Security.Claims;
using System.Text.Encodings.Web;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HomeShowing.Identity.Infrastructure;

public static class AuthSchemes
{
    public const string Smart = "Smart";
    public const string Pat = "Pat";
}

/// <summary>Authenticates <c>Authorization: Bearer hss_pat_...</c> personal access tokens (MCP clients).
/// The token's tenant and role come from the stored user, so a token can never act outside them.</summary>
public sealed class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    CredentialStore store, IClock clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer " + PatFormat.Prefix, StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var raw = header["Bearer ".Length..].Trim();
        var token = await store.FindTokenByHashAsync(PatFormat.Hash(raw), Context.RequestAborted);
        if (token is null || token.RevokedAt is not null) return AuthenticateResult.Fail("Token is invalid or revoked.");
        var user = await store.FindUserAsync(token.TenantId, token.UserId, Context.RequestAborted);
        if (user is null) return AuthenticateResult.Fail("Token owner no longer exists.");

        var now = clock.GetCurrentInstant();
        if (token.LastUsedAt is null || now - token.LastUsedAt.Value > Duration.FromMinutes(1))
            await store.TouchTokenAsync(token.Id, now, Context.RequestAborted);

        var identity = new ClaimsIdentity([
            new Claim(AppClaims.Sub, user.Id), new Claim(AppClaims.TenantId, user.TenantId), new Claim(AppClaims.Role, user.Role),
            new Claim(AppClaims.Name, user.Name), new Claim(AppClaims.Email, user.Email), new Claim(AppClaims.TimeZone, user.TimeZone),
            new Claim("auth", "pat"),
        ], Scheme.Name, AppClaims.Name, AppClaims.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>Copies the authenticated claims into the scoped <see cref="TenantContext"/> that every repository reads.</summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        var u = http.User;
        if (u.Identity?.IsAuthenticated == true && u.FindFirst(AppClaims.TenantId)?.Value is { } tid && u.FindFirst(AppClaims.Sub)?.Value is { } sub)
            tenant.Set(tid, sub, u.FindFirst(AppClaims.Role)?.Value ?? "", u.FindFirst(AppClaims.Name)?.Value, u.FindFirst(AppClaims.TimeZone)?.Value);
        return next(http);
    }
}
