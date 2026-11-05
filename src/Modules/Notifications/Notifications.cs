using HomeShowing.BuildingBlocks.Events;
using HomeShowing.BuildingBlocks.Outbox;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Identity.Application;
using HomeShowing.Scheduling.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace HomeShowing.Notifications;

/// <summary>The email a handler wants sent. Stored as the outbox payload; delivered later by the Worker.</summary>
public sealed record EmailIntent(string To, string ToName, string Subject, string Body, string ShowingId);

public static class OutboxTypes
{
    public const string Email = "email.send";
}

/// <summary>
/// Turns scheduling events into outbox rows. Runs inside the booking / confirm / decline transaction (the
/// dispatcher is in process and the outbox enlists in the scoped session), so an email is queued if and only if
/// the state change committed. Delivery is the Worker's job.
/// </summary>
public sealed class ShowingEmailHandlers(IOutbox outbox, IUserDirectory users) :
    IDomainEventHandler<ShowingRequested>, IDomainEventHandler<ShowingConfirmed>,
    IDomainEventHandler<ShowingDeclined>, IDomainEventHandler<ShowingCancelled>
{
    public async Task HandleAsync(ShowingRequested e, CancellationToken ct)
    {
        var when = Label(e.Showing);
        await EmailAsync(e.TenantId, e.Showing.AgentId, e.Showing.ShowingId,
            e.AutoConfirmed ? $"Showing booked: {e.Showing.ListingAddress}" : $"New showing request: {e.Showing.ListingAddress}",
            $"{e.Showing.BuyerName} {(e.AutoConfirmed ? "booked" : "requested")} a showing of {e.Showing.ListingAddress} on {when}." +
            (e.AutoConfirmed ? " It was confirmed automatically." : " Approve or decline it in your inbox."), ct);
        if (e.AutoConfirmed)
            await EmailAsync(e.TenantId, e.Showing.BuyerId, e.Showing.ShowingId, $"Showing confirmed: {e.Showing.ListingAddress}",
                $"Your showing of {e.Showing.ListingAddress} on {when} is confirmed. {e.Showing.AgentName} will meet you there.", ct);
    }

    public Task HandleAsync(ShowingConfirmed e, CancellationToken ct) =>
        EmailAsync(e.TenantId, e.Showing.BuyerId, e.Showing.ShowingId, $"Showing confirmed: {e.Showing.ListingAddress}",
            $"Good news: {e.Showing.AgentName} confirmed your showing of {e.Showing.ListingAddress} on {Label(e.Showing)}.", ct);

    public Task HandleAsync(ShowingDeclined e, CancellationToken ct) =>
        EmailAsync(e.TenantId, e.Showing.BuyerId, e.Showing.ShowingId, $"Showing declined: {e.Showing.ListingAddress}",
            $"{e.Showing.AgentName} could not do {Label(e.Showing)} at {e.Showing.ListingAddress}. Pick another open slot in the app.", ct);

    public Task HandleAsync(ShowingCancelled e, CancellationToken ct)
    {
        var buyerCancelled = e.CancelledByRole == "Buyer";
        return EmailAsync(e.TenantId, buyerCancelled ? e.Showing.AgentId : e.Showing.BuyerId, e.Showing.ShowingId,
            $"Showing cancelled: {e.Showing.ListingAddress}",
            $"The showing of {e.Showing.ListingAddress} on {Label(e.Showing)} was cancelled by the {(buyerCancelled ? "buyer" : "agent")}.", ct);
    }

    private async Task EmailAsync(string tenantId, string userId, string showingId, string subject, string body, CancellationToken ct)
    {
        var contact = await users.FindContactAsync(tenantId, userId, ct);
        if (contact is null) return;
        await outbox.EnqueueAsync(tenantId, OutboxTypes.Email, new EmailIntent(contact.Value.Email, contact.Value.Name, subject, body, showingId), ct);
    }

    private static string Label(ShowingSnapshot s) =>
        SlotFormat.Label(s.SlotStartUtc, DateTimeZoneProviders.Tzdb.GetZoneOrNull(s.ListingTimeZone) ?? DateTimeZone.Utc);
}

/// <summary>A delivered email, kept for the admin "Outbox and emails" page.</summary>
public sealed class SentEmail
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string TenantId { get; set; } = "";
    public string OutboxId { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Transport { get; set; } = "";
    public Instant SentAt { get; set; }
}

public static class NotificationsModule
{
    public const string SentEmailsCollection = "sentEmails";

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<ShowingEmailHandlers>();
        services.AddScoped<IDomainEventHandler<ShowingRequested>>(sp => sp.GetRequiredService<ShowingEmailHandlers>());
        services.AddScoped<IDomainEventHandler<ShowingConfirmed>>(sp => sp.GetRequiredService<ShowingEmailHandlers>());
        services.AddScoped<IDomainEventHandler<ShowingDeclined>>(sp => sp.GetRequiredService<ShowingEmailHandlers>());
        services.AddScoped<IDomainEventHandler<ShowingCancelled>>(sp => sp.GetRequiredService<ShowingEmailHandlers>());
        services.AddSingleton<IMongoSchemaSetup, NotificationsSchema>();
        return services;
    }
}

internal sealed class NotificationsSchema : IMongoSchemaSetup
{
    public async Task ApplyAsync(MongoDb db, CancellationToken ct)
    {
        await MongoSchema.EnsureCollectionAsync(db.Database, NotificationsModule.SentEmailsCollection, null, ct);
        var col = db.Collection<SentEmail>(NotificationsModule.SentEmailsCollection);
        await col.Indexes.CreateManyAsync([
            // Idempotency: one delivery per outbox row, even if the worker retries after a crash.
            new CreateIndexModel<SentEmail>(Builders<SentEmail>.IndexKeys.Ascending(x => x.OutboxId), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<SentEmail>(Builders<SentEmail>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.SentAt)),
        ], ct);
    }
}
