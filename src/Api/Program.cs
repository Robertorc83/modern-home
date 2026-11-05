using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HomeShowing.Ai;
using HomeShowing.Api.Seed;
using HomeShowing.BuildingBlocks;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Identity;
using HomeShowing.Identity.Application;
using HomeShowing.Identity.Endpoints;
using HomeShowing.Identity.Infrastructure;
using HomeShowing.Listings;
using HomeShowing.Listings.Endpoints;
using HomeShowing.Mcp;
using HomeShowing.Notifications;
using HomeShowing.Scheduling;
using HomeShowing.Scheduling.Endpoints;
using HomeShowing.Tenancy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using MongoDB.Bson;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi();
var authPerMinute = config.GetValue("RateLimits:AuthPerMinute", 60);
var aiPerMinute = config.GetValue("RateLimits:AiPerMinute", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Keyed by client IP plus the email being tried (see AuthRateLimitKey): behind the Next.js BFF every request shares
    // one proxy IP, so IP alone would be a single global bucket for all sign ins.
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(AuthRateLimitKey.For(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Per user: UseRateLimiter runs after UseAuthentication so the sub claim is populated here.
    o.AddPolicy("ai", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirst("sub")?.Value ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = aiPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// Composition root: each module registers its own services, schema and endpoints.
builder.Services
    .AddBuildingBlocks(config)
    .AddTenancyModule()
    .AddIdentityModule(config, builder.Environment.EnvironmentName)
    .AddListingsModule()
    .AddSchedulingModule()
    .AddNotificationsModule()
    .AddAiModule(config)
    .AddMcpModule();
builder.Services.AddScoped<DemoSeed>();

var app = builder.Build();

if (app.Services.GetRequiredService<JwtOptions>().IsDevKey)
    app.Logger.LogWarning("Auth:SigningKey is the public development key; anyone can forge sessions. Set Auth__SigningKey (JWT_SIGNING_KEY) before exposing this API.");

// The API sits behind the Next.js BFF (and any ingress in front of it), so take the client address from X-Forwarded-For.
// KnownNetworks/KnownProxies are cleared for the Docker demo, which means any direct caller can spoof the header;
// a real deployment should list its proxy addresses here instead.
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException b ? b.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.Use(AuthRateLimitKey.Middleware);
app.UseAuthentication();
app.UseRateLimiter(); // after authentication so per user policies ("ai") see the caller
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/scalar", o => o.WithTitle("Home Showing Scheduler API"));
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapGet("/health", async (MongoDb db, CancellationToken ct) =>
{
    await db.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    return TypedResults.Ok(new { status = "ok", mongo = "ok" });
}).WithTags("Ops").AllowAnonymous();

app.MapIdentityEndpoints();
app.MapListingEndpoints();
app.MapSchedulingEndpoints();
app.MapNotificationEndpoints();
app.MapAiEndpoints();
app.MapMcpEndpoints();

await app.Services.ApplyMongoSchemaAsync();
if (config.GetValue("Seed:Enabled", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoSeed>().RunAsync(CancellationToken.None);
}

app.Run();

public partial class Program;

/// <summary>
/// Partition key for the "auth" rate limiter: the client IP (after forwarded headers) plus the lowercased email in a
/// sign in / register body. The email part keeps one account from being brute forced even when every request arrives
/// from the same proxy address; the IP part still bounds a single caller spraying many emails.
/// </summary>
internal static class AuthRateLimitKey
{
    private const string Item = "auth.rateLimitKey";
    private const int MaxBodyBytes = 16 * 1024;

    public static string For(HttpContext ctx) =>
        ctx.Items.TryGetValue(Item, out var key) && key is string s ? s : Ip(ctx);

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "x";

    /// <summary>Runs before the rate limiter: buffers the small JSON body of the auth endpoints and records the key.</summary>
    public static async Task Middleware(HttpContext ctx, RequestDelegate next)
    {
        if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/auth"))
            ctx.Items[Item] = await ReadEmailAsync(ctx) is { Length: > 0 } email ? $"{Ip(ctx)}|{email}" : Ip(ctx);
        await next(ctx);
    }

    private static async Task<string?> ReadEmailAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (!req.HasJsonContentType() || req.ContentLength is null or > MaxBodyBytes) return null;
        req.EnableBuffering();
        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ctx.RequestAborted);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("email", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return p.Value.GetString()?.Trim().ToLowerInvariant();
            return null;
        }
        catch (System.Text.Json.JsonException) { return null; } // model binding will report the malformed body
        finally { req.Body.Position = 0; }
    }
}
