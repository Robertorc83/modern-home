using HomeShowing.BuildingBlocks.Web;
using HomeShowing.Identity.Application;
using HomeShowing.Identity.Infrastructure;
using HomeShowing.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HomeShowing.Identity.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Identity");
        auth.MapPost("/login", async (LoginRequest req, AuthService svc, CancellationToken ct) => (await svc.LoginAsync(req, ct)).ToHttp())
            .AllowAnonymous().RequireRateLimiting("auth");
        auth.MapPost("/register-buyer", async (RegisterBuyerRequest req, AuthService svc, CancellationToken ct) =>
            (await svc.RegisterBuyerAsync(req, ct)).ToHttp(r => TypedResults.Created("/auth/me", r))).AllowAnonymous().RequireRateLimiting("auth");
        auth.MapGet("/me", async (AuthService svc, CancellationToken ct) => (await svc.MeAsync(ct)).ToHttp()).RequireAuthorization();

        // Token management needs a web session: a personal access token must never mint or revoke tokens.
        var tokens = auth.MapGroup("/tokens").RequireAuthorization("BuyerOrAgent", NotPersonalAccessTokenRequirement.Policy);
        tokens.MapGet("/", async (AuthService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListTokensAsync(ct)));
        tokens.MapPost("/", async (CreateTokenRequest req, AuthService svc, CancellationToken ct) =>
            (await svc.CreateTokenAsync(req, ct)).ToHttp(t => TypedResults.Created($"/auth/tokens/{t.Id}", t)));
        tokens.MapDelete("/{id}", async (string id, AuthService svc, CancellationToken ct) =>
            (await svc.RevokeTokenAsync(id, ct)).ToHttp(_ => TypedResults.NoContent()));

        app.MapGet("/tenants", async (ITenantDirectory dir, CancellationToken ct) =>
            TypedResults.Ok((await dir.ListAsync(ct)).Select(t => new { t.Slug, t.Name, t.DefaultTimeZone }))).WithTags("Identity").AllowAnonymous();

        var admin = app.MapGroup("/admin/agents").WithTags("Admin").RequireAuthorization("Admin", NotPersonalAccessTokenRequirement.Policy);
        admin.MapGet("/", async (AuthService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAgentsAsync(ct)));
        admin.MapPost("/", async (CreateAgentRequest req, AuthService svc, CancellationToken ct) =>
            (await svc.CreateAgentAsync(req, ct)).ToHttp(a => TypedResults.Created($"/admin/agents/{a.Id}", a)));
        return app;
    }
}
