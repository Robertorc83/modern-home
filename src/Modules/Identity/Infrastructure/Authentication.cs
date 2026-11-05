using System.Security.Claims;
using System.Text.Encodings.Web;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
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
    /// <summary>Claim that marks a principal as PAT authenticated (see <see cref="NotPersonalAccessTokenRequirement"/>).</summary>
    public const string AuthClaim = "auth";
    public const string AuthClaimPat = "pat";

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
            new Claim(AuthClaim, AuthClaimPat),
        ], Scheme.Name, AppClaims.Name, AppClaims.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>
/// Blocks personal access tokens from endpoints that must only be reached by a signed in web session: token
/// management (a PAT must not be able to mint more PATs) and admin screens. PATs still work on /mcp and the
/// read/booking REST endpoints.
/// </summary>
public sealed class NotPersonalAccessTokenRequirement : IAuthorizationRequirement
{
    public const string Policy = "NoPat";
    public const string Message = "Personal access tokens cannot manage tokens; sign in to the web app.";
}

public sealed class NotPersonalAccessTokenHandler : AuthorizationHandler<NotPersonalAccessTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NotPersonalAccessTokenRequirement requirement)
    {
        if (context.User.FindFirst(PatAuthenticationHandler.AuthClaim)?.Value == PatAuthenticationHandler.AuthClaimPat)
            context.Fail(new AuthorizationFailureReason(this, NotPersonalAccessTokenRequirement.Message));
        else context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Turns a PAT rejection into a 403 ProblemDetails with an explanation; everything else keeps the default behaviour.</summary>
public sealed class PatAwareAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var failure = authorizeResult.AuthorizationFailure;
        var patRejected = failure is not null &&
            (failure.FailureReasons.Any(r => r.Handler is NotPersonalAccessTokenHandler) || failure.FailedRequirements.OfType<NotPersonalAccessTokenRequirement>().Any());
        if (authorizeResult.Forbidden && patRejected)
            return Results.Problem(title: "Forbidden", detail: NotPersonalAccessTokenRequirement.Message, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
        return _default.HandleAsync(next, context, policy, authorizeResult);
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
