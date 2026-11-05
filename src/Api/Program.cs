using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HomeShowing.Ai;
using HomeShowing.Api.Seed;
using HomeShowing.BuildingBlocks;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Identity;
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
var authPerMinute = config.GetValue("RateLimits:AuthPerMinute", 30);
var aiPerMinute = config.GetValue("RateLimits:AiPerMinute", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ai", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirst("sub")?.Value ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = aiPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// Composition root: each module registers its own services, schema and endpoints.
builder.Services
    .AddBuildingBlocks(config)
    .AddTenancyModule()
    .AddIdentityModule(config)
    .AddListingsModule()
    .AddSchedulingModule()
    .AddNotificationsModule()
    .AddAiModule(config)
    .AddMcpModule();
builder.Services.AddScoped<DemoSeed>();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException b ? b.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
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
