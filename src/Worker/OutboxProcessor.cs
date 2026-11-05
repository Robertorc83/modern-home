using HomeShowing.BuildingBlocks.Outbox;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Notifications;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NodaTime;

namespace HomeShowing.Worker;

public interface IEmailSender
{
    string Transport { get; }
    Task SendAsync(EmailIntent email, CancellationToken ct);
}

/// <summary>Local delivery: logs the email. SES replaces it behind the same interface when AWS is enabled.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public string Transport => "dev-log";
    public Task SendAsync(EmailIntent email, CancellationToken ct)
    {
        log.LogInformation("EMAIL to {To}: {Subject}", email.To, email.Subject);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The outbox worker. Two modes share the same delivery code:
/// <list type="bullet">
/// <item><b>Direct</b> (no AWS): claim an outbox row with an atomic lease and deliver it in process.</item>
/// <item><b>Queued</b> (AWS): <see cref="RelayNextAsync"/> publishes claimed rows to SQS, and <see cref="DeliverAsync"/>
/// is called by the queue consumer.</item>
/// </list>
/// Delivery is at least once with a dedup record: <c>sentEmails</c> has a unique index on the outbox id and is checked
/// before sending, so a retry after a crash (or a duplicate SQS delivery) normally skips the send. The dedup record is
/// written only after the provider accepts the email, so a crash in the window between the send and that insert
/// (or a provider timeout whose email was still delivered) can produce a duplicate on retry. True exactly once would
/// need an idempotency key the email provider honours; SES does not offer one.
/// Failures back off exponentially up to <see cref="MaxAttempts"/>;
/// a row that gives up keeps its last error, visible on the admin "Outbox and emails" page.
/// </summary>
public sealed class OutboxProcessor(MongoDb db, IEmailSender sender, IClock clock, ILogger<OutboxProcessor> log, bool direct = true) : BackgroundService
{
    public const int MaxAttempts = 5;
    private static readonly Duration Lease = Duration.FromSeconds(30);

    private IMongoCollection<OutboxMessage> Outbox => db.Collection<OutboxMessage>(MongoOutbox.CollectionName);
    private IMongoCollection<SentEmail> Sent => db.Collection<SentEmail>(NotificationsModule.SentEmailsCollection);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!direct) return;
        log.LogInformation("Outbox worker started (direct delivery, transport {Transport})", sender.Transport);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await ProcessNextAsync(stoppingToken);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>Direct mode: claim, deliver, mark processed.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var msg = await ClaimAsync(ct);
        if (msg is null) return false;
        await DeliverAsync(msg, ct);
        return true;
    }

    /// <summary>Queued mode, producer side: claim a row and hand it to <paramref name="publish"/>; mark it published.</summary>
    public async Task<bool> RelayNextAsync(Func<OutboxMessage, CancellationToken, Task> publish, CancellationToken ct)
    {
        var msg = await ClaimAsync(ct, unpublishedOnly: true);
        if (msg is null) return false;
        try
        {
            await publish(msg, ct);
            await Outbox.UpdateOneAsync(m => m.Id == msg.Id, Builders<OutboxMessage>.Update
                .Set(m => m.PublishedAt, clock.GetCurrentInstant()).Unset(m => m.LockedUntil).Set(m => m.LastError, null), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(msg, ex, ct);
        }
        return true;
    }

    /// <summary>
    /// Queued mode, consumer side. Returns true when the message can be acknowledged (delivered, or nothing to do).
    /// The row is leased atomically before sending (ProcessedAt null and no live lock), so two consumers holding the
    /// same SQS message (a redelivery while the first is still sending) cannot both send: the second sees the lock,
    /// returns false and SQS redelivers later, by which time the row is processed and gets acknowledged.
    /// What is still at least once: a send that outlives the lease (30 s) plus the SQS visibility timeout (60 s) could
    /// let a redelivery lease the row again, and a crash between the provider accepting the email and the sentEmails
    /// insert leaves no record of the send. Both need an idempotency key the provider honours; SES has none.
    /// </summary>
    public async Task<bool> DeliverAsync(string outboxId, CancellationToken ct)
    {
        // The relay holds the same lease while it publishes to SQS and releases it right after; the consumer can
        // receive the message inside that window, so a locked, unpublished row is retried briefly before giving up.
        for (var attempt = 0; ; attempt++)
        {
            var msg = await TryLeaseAsync(outboxId, ct);
            if (msg is not null) return await DeliverAsync(msg, ct);
            var current = await Outbox.Find(m => m.Id == outboxId).FirstOrDefaultAsync(ct);
            if (current is null || current.ProcessedAt is not null) return true; // unknown or already delivered: ack
            if (current.PublishedAt is not null || attempt >= 5) return false;    // leased by another consumer: SQS redelivers
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    private Task<OutboxMessage?> TryLeaseAsync(string outboxId, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var f = Builders<OutboxMessage>.Filter;
        var free = f.And(f.Eq(m => m.Id, outboxId), f.Eq(m => m.ProcessedAt, null), f.Or(f.Eq(m => m.LockedUntil, null), f.Lt(m => m.LockedUntil, now)));
        return Outbox.FindOneAndUpdateAsync(free, Builders<OutboxMessage>.Update.Set(m => m.LockedUntil, now + Lease).Inc(m => m.Attempts, 1),
            new FindOneAndUpdateOptions<OutboxMessage> { ReturnDocument = ReturnDocument.After }, ct)!;
    }

    private async Task<OutboxMessage?> ClaimAsync(CancellationToken ct, bool unpublishedOnly = false)
    {
        var now = clock.GetCurrentInstant();
        var f = Builders<OutboxMessage>.Filter;
        var claim = f.And(f.Eq(m => m.ProcessedAt, null), f.Lt(m => m.Attempts, MaxAttempts),
            f.Or(f.Eq(m => m.LockedUntil, null), f.Lt(m => m.LockedUntil, now)));
        if (unpublishedOnly) claim = f.And(claim, f.Eq(m => m.PublishedAt, null));
        return await Outbox.FindOneAndUpdateAsync(claim,
            Builders<OutboxMessage>.Update.Set(m => m.LockedUntil, now + Lease).Inc(m => m.Attempts, 1),
            new FindOneAndUpdateOptions<OutboxMessage> { Sort = Builders<OutboxMessage>.Sort.Ascending(m => m.OccurredAt), ReturnDocument = ReturnDocument.After }, ct);
    }

    private async Task<bool> DeliverAsync(OutboxMessage msg, CancellationToken ct)
    {
        try
        {
            if (msg.Type == OutboxTypes.Email)
            {
                var email = BsonSerializer.Deserialize<EmailIntent>(msg.Payload);
                var alreadySent = await Sent.Find(s => s.OutboxId == msg.Id).AnyAsync(ct);
                if (!alreadySent)
                {
                    await sender.SendAsync(email, ct);
                    try
                    {
                        await Sent.InsertOneAsync(new SentEmail
                        {
                            TenantId = msg.TenantId, OutboxId = msg.Id, To = email.To, Subject = email.Subject, Body = email.Body,
                            Transport = sender.Transport, SentAt = clock.GetCurrentInstant(),
                        }, cancellationToken: ct);
                    }
                    catch (Exception ex) when (MongoErrors.IsDuplicateKey(ex)) { /* another worker recorded it */ }
                }
            }
            await Outbox.UpdateOneAsync(m => m.Id == msg.Id, Builders<OutboxMessage>.Update
                .Set(m => m.ProcessedAt, clock.GetCurrentInstant()).Unset(m => m.LockedUntil).Set(m => m.LastError, null), cancellationToken: ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(msg, ex, ct);
            return false;
        }
    }

    private async Task FailAsync(OutboxMessage msg, Exception ex, CancellationToken ct)
    {
        var backoff = Duration.FromSeconds(Math.Pow(2, msg.Attempts) * 5);
        log.LogWarning(ex, "Outbox message {Id} failed (attempt {Attempt})", msg.Id, msg.Attempts);
        await Outbox.UpdateOneAsync(m => m.Id == msg.Id, Builders<OutboxMessage>.Update
            .Set(m => m.LockedUntil, clock.GetCurrentInstant() + backoff).Set(m => m.LastError, ex.Message), cancellationToken: ct);
    }
}
