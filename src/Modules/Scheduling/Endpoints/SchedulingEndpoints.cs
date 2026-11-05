using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.BuildingBlocks.Web;
using HomeShowing.Scheduling.Application;
using HomeShowing.Scheduling.Contracts;
using HomeShowing.Scheduling.Domain;
using HomeShowing.Scheduling.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NodaTime;

namespace HomeShowing.Scheduling.Endpoints;

public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/listings/{id}/slots", async (string id, string? from, string? to, string? timeZone, AvailabilityService svc, CancellationToken ct) =>
        {
            Instant? f = BookingService.TryParseInstant(from, out var fi) ? fi : null;
            Instant? t = BookingService.TryParseInstant(to, out var ti) ? ti : null;
            return (await svc.GetListingSlotsAsync(id, timeZone, f, t, ct)).ToHttp();
        }).WithTags("Listings").RequireAuthorization();

        app.MapGet("/listings/{id}/showings", async (string id, BookingService svc, CancellationToken ct) =>
            (await svc.ForListingAsync(id, ct)).ToHttp()).WithTags("Listings").RequireAuthorization("AgentOrAdmin");

        app.MapPost("/match", async (MatchRequest req, AvailabilityService svc, CancellationToken ct) =>
            (await svc.MatchAsync(req, ct)).ToHttp()).WithTags("Matching").RequireAuthorization("Buyer");

        var g = app.MapGroup("/showings").WithTags("Showings").RequireAuthorization();

        g.MapPost("/holds", async (HoldRequest req, BookingService svc, CancellationToken ct) =>
            (await svc.HoldAsync(req.ListingId, req.SlotStart, req.TimeZone, ct)).ToHttp(h => TypedResults.Created($"/showings/holds/{h.Id}", h))).RequireAuthorization("Buyer");

        g.MapDelete("/holds/{id}", async (string id, BookingService svc, CancellationToken ct) =>
            (await svc.ReleaseHoldAsync(id, ct)).ToHttp(_ => TypedResults.NoContent())).RequireAuthorization("Buyer");

        g.MapPost("/", async (BookRequest req, BookingService svc, CancellationToken ct) =>
            (await svc.BookAsync(req with { Source = "web" }, ct)).ToHttp(s => TypedResults.Created($"/showings/{s.Id}", s))).RequireAuthorization("Buyer");

        g.MapPost("/{id}/confirm", async (string id, BookingService svc, CancellationToken ct) => (await svc.ConfirmAsync(id, ct)).ToHttp()).RequireAuthorization("AgentOrAdmin");
        g.MapPost("/{id}/decline", async (string id, BookingService svc, CancellationToken ct) => (await svc.DeclineAsync(id, ct)).ToHttp()).RequireAuthorization("AgentOrAdmin");
        g.MapPost("/{id}/cancel", async (string id, BookingService svc, CancellationToken ct) => (await svc.CancelAsync(id, ct)).ToHttp());

        g.MapGet("/mine", async (BookingService svc, CancellationToken ct) => TypedResults.Ok(await svc.MineAsync(ct))).RequireAuthorization("Buyer");
        g.MapGet("/inbox", async (ShowingStatus? status, BookingService svc, CancellationToken ct) => (await svc.InboxAsync(status, ct)).ToHttp())
            .RequireAuthorization("AgentOrAdmin");

        // Server Sent Events over a MongoDB change stream: the agent inbox and "My showings" update live.
        // Authentication is checked once at connect, so the stream ends itself when the JWT expires (the client
        // reconnects with a fresh token), and each user may hold at most MaxStreamsPerUser connections.
        g.MapGet("/stream", async (HttpContext http, ShowingChangeFeed feed, ITenantContext tenant, IClock clock, CancellationToken ct) =>
        {
            var user = tenant.UserId;
            if (Streams.AddOrUpdate(user, 1, (_, n) => n + 1) > MaxStreamsPerUser)
            {
                Release(user);
                return Results.Problem(title: "Too many live streams", detail: $"At most {MaxStreamsPerUser} concurrent /showings/stream connections per user.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
            using var life = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                // JWTs carry exp (unix seconds; MapInboundClaims is off so the claim keeps its name). PATs have no exp: cap only.
                if (long.TryParse(http.User.FindFirst("exp")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
                {
                    var remaining = Instant.FromUnixTimeSeconds(exp) - clock.GetCurrentInstant();
                    if (remaining <= Duration.Zero) return Results.Problem(title: "Session expired", statusCode: StatusCodes.Status401Unauthorized);
                    life.CancelAfter(remaining.ToTimeSpan());
                }
                var token = life.Token;
                http.Response.Headers.ContentType = "text/event-stream";
                http.Response.Headers.CacheControl = "no-cache, no-transform";
                http.Response.Headers["X-Accel-Buffering"] = "no";
                var resume = http.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? http.Request.Query["lastEventId"].FirstOrDefault();
                await http.Response.WriteAsync("retry: 3000\n: connected\n\n", token);
                await http.Response.Body.FlushAsync(token);
                try
                {
                    await foreach (var change in feed.WatchAsync(resume, token))
                    {
                        if (change is null) { await http.Response.WriteAsync(": ping\n\n", token); }
                        else
                        {
                            var data = JsonSerializer.Serialize(new { showingId = change.ShowingId, status = change.Status, operation = change.Operation, listingAddress = change.ListingAddress, buyerName = change.BuyerName });
                            await http.Response.WriteAsync($"id: {change.ResumeToken}\nevent: showing\ndata: {data}\n\n", token);
                        }
                        await http.Response.Body.FlushAsync(token);
                    }
                }
                catch (OperationCanceledException) { /* client went away, or the token expired */ }
                return Results.Empty;
            }
            finally { Release(user); }
        }).ExcludeFromDescription();

        return app;
    }

    public const int MaxStreamsPerUser = 5;
    /// <summary>Open /showings/stream connections per user id (process local; one API instance in the demo).</summary>
    private static readonly ConcurrentDictionary<string, int> Streams = new();

    private static void Release(string user)
    {
        if (Streams.AddOrUpdate(user, 0, (_, n) => n - 1) <= 0) Streams.TryRemove(new KeyValuePair<string, int>(user, 0));
    }
}
