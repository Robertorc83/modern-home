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

/// <summary>Local delivery: logs the email. The AWS design swaps in SES behind the same interface (docs/aws-deployment.md).</summary>
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
/// Delivers outbox rows. Claims one row at a time with an atomic lease (so several workers can run), records the
/// delivery under a unique index on the outbox id (so a retry after a crash never sends twice), then marks the row
/// processed. Failures back off exponentially and give up after <see cref="MaxAttempts"/>, leaving the row and its
/// error visible on the admin page (a dead letter, in effect).
/// </summary>
public sealed class OutboxProcessor(MongoDb db, IEmailSender sender, IClock clock, ILogger<OutboxProcessor> log) : BackgroundService
{
    public const int MaxAttempts = 5;
    private static readonly Duration Lease = Duration.FromSeconds(30);

    private IMongoCollection<OutboxMessage> Outbox => db.Collection<OutboxMessage>(MongoOutbox.CollectionName);
    private IMongoCollection<SentEmail> Sent => db.Collection<SentEmail>(NotificationsModule.SentEmailsCollection);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Outbox worker started (transport {Transport})", sender.Transport);
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

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var f = Builders<OutboxMessage>.Filter;
        var claim = f.And(f.Eq(m => m.ProcessedAt, null), f.Lt(m => m.Attempts, MaxAttempts),
            f.Or(f.Eq(m => m.LockedUntil, null), f.Lt(m => m.LockedUntil, now)));
        var msg = await Outbox.FindOneAndUpdateAsync(claim,
            Builders<OutboxMessage>.Update.Set(m => m.LockedUntil, now + Lease).Inc(m => m.Attempts, 1),
            new FindOneAndUpdateOptions<OutboxMessage> { Sort = Builders<OutboxMessage>.Sort.Ascending(m => m.OccurredAt), ReturnDocument = ReturnDocument.After }, ct);
        if (msg is null) return false;

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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var backoff = Duration.FromSeconds(Math.Pow(2, msg.Attempts) * 5);
            log.LogWarning(ex, "Outbox message {Id} failed (attempt {Attempt})", msg.Id, msg.Attempts);
            await Outbox.UpdateOneAsync(m => m.Id == msg.Id, Builders<OutboxMessage>.Update
                .Set(m => m.LockedUntil, clock.GetCurrentInstant() + backoff).Set(m => m.LastError, ex.Message), cancellationToken: ct);
        }
        return true;
    }
}
