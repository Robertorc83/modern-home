using System.Text.Json;
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
            (await svc.HoldAsync(req.ListingId, req.SlotStart, ct)).ToHttp(h => TypedResults.Created($"/showings/holds/{h.Id}", h))).RequireAuthorization("Buyer");

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
        g.MapGet("/stream", async (HttpContext http, ShowingChangeFeed feed, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache, no-transform";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            var resume = http.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? http.Request.Query["lastEventId"].FirstOrDefault();
            await http.Response.WriteAsync("retry: 3000\n: connected\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
            try
            {
                await foreach (var change in feed.WatchAsync(resume, ct))
                {
                    if (change is null) { await http.Response.WriteAsync(": ping\n\n", ct); }
                    else
                    {
                        var data = JsonSerializer.Serialize(new { showingId = change.ShowingId, status = change.Status, operation = change.Operation, listingAddress = change.ListingAddress, buyerName = change.BuyerName });
                        await http.Response.WriteAsync($"id: {change.ResumeToken}\nevent: showing\ndata: {data}\n\n", ct);
                    }
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client went away */ }
        }).ExcludeFromDescription();

        return app;
    }
}
