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
// Two independent limiters guard the anonymous sign in / register endpoints (see AuthRateLimitKey):
//  - per client IP (RateLimits:AuthPerMinute, default 60/min), a global limiter so one address cannot escape it by
//    spraying many emails;
//  - per email (RateLimits:AuthPerEmailPerMinute, default 10/min, any IP), so one account cannot be brute forced
//    from many addresses or from behind the shared BFF proxy address.
// The AI parser limit lives inside AvailabilityParsingService (shared by REST and MCP), not here.
var authPerIpPerMinute = config.GetValue("RateLimits:AuthPerMinute", 60);
var authPerEmailPerMinute = config.GetValue("RateLimits:AuthPerEmailPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => AuthRateLimitKey.IsAuthEndpoint(ctx)
        ? RateLimitPartition.GetFixedWindowLimiter("ip|" + AuthRateLimitKey.Ip(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerIpPerMinute, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("none"));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(AuthRateLimitKey.EmailKey(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerEmailPerMinute, Window = TimeSpan.FromMinutes(1) }));
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

// The API sits behind the Next.js BFF (and any ingress in front of it). X-Forwarded-For is honoured only when the
// connection comes from a configured trusted proxy; otherwise the connection address is the client address, so a
// direct caller (the API port is published in docker-compose) cannot pick its own address for the per IP limiter.
//   Proxy__KnownProxies="10.0.1.5,10.0.1.6"        exact proxy addresses
//   Proxy__KnownNetworks="172.16.0.0/12,10.0.0.0/8" CIDR ranges, e.g. the Docker bridge network where the web
//                                                    container's address is dynamic
// Default: none. Without them every request through the BFF shares the proxy's address (one per IP bucket).
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
foreach (var p in (config["Proxy:KnownProxies"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(p));
foreach (var n in (config["Proxy:KnownNetworks"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(n));
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
/// Partition keys for the sign in / register rate limiters.
/// <list type="bullet">
/// <item><see cref="Ip"/>: the connection address, replaced by X-Forwarded-For only when the connection comes from a
/// trusted proxy (see the ForwardedHeadersOptions setup).</item>
/// <item><see cref="EmailKey"/>: the lowercased <c>email</c> in the JSON body. When the key appears more than once the
/// LAST occurrence wins, which is what System.Text.Json model binding uses, so the limiter and the handler always
/// look at the same account. A body without a usable email falls back to the address.</item>
/// </list>
/// </summary>
internal static class AuthRateLimitKey
{
    private const string Item = "auth.rateLimitEmail";
    private const int MaxBodyBytes = 16 * 1024;

    public static bool IsAuthEndpoint(HttpContext ctx) =>
        HttpMethods.IsPost(ctx.Request.Method) &&
        (ctx.Request.Path.Equals("/auth/login", StringComparison.OrdinalIgnoreCase) || ctx.Request.Path.Equals("/auth/register-buyer", StringComparison.OrdinalIgnoreCase));

    public static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "x";

    public static string EmailKey(HttpContext ctx) =>
        ctx.Items.TryGetValue(Item, out var key) && key is string s ? "email|" + s : "noemail|" + Ip(ctx);

    /// <summary>Runs before the rate limiter: buffers the small JSON body of the auth endpoints and records the email.</summary>
    public static async Task Middleware(HttpContext ctx, RequestDelegate next)
    {
        if (IsAuthEndpoint(ctx) && await ReadEmailAsync(ctx) is { Length: > 0 } email)
            ctx.Items[Item] = email;
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
            string? email = null;
            foreach (var p in doc.RootElement.EnumerateObject()) // last "email" wins, matching the model binder
                if (p.Name.Equals("email", StringComparison.OrdinalIgnoreCase))
                    email = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString()?.Trim().ToLowerInvariant() : null;
            return email;
        }
        catch (System.Text.Json.JsonException) { return null; } // model binding will report the malformed body
        finally { req.Body.Position = 0; }
    }
}
