using HomeShowing.BuildingBlocks.Outbox;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MongoDB.Driver;

namespace HomeShowing.Notifications;

public static class OutboxEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/outbox", async (MongoDb db, ITenantContext tenant, HomeShowing.BuildingBlocks.Aws.AwsOptions aws, CancellationToken ct) =>
        {
            var outbox = await db.Collection<OutboxMessage>(MongoOutbox.CollectionName)
                .Find(m => m.TenantId == tenant.TenantId).SortByDescending(m => m.OccurredAt).Limit(50).ToListAsync(ct);
            var emails = await db.Collection<SentEmail>(NotificationsModule.SentEmailsCollection)
                .Find(m => m.TenantId == tenant.TenantId).SortByDescending(m => m.SentAt).Limit(50).ToListAsync(ct);
            return TypedResults.Ok(new
            {
                outbox = outbox.Select(m => new
                {
                    m.Id, m.Type, m.OccurredAt, m.ProcessedAt, m.Attempts, m.LastError,
                    to = m.Payload.GetValue("to", "").ToString(), subject = m.Payload.GetValue("subject", "").ToString(),
                }),
                emails = emails.Select(e => new { e.Id, e.To, e.Subject, e.Body, e.Transport, e.SentAt }),
                transport = aws.Enabled ? "ses" : "dev-log",
                // LocalStack exposes every SES message it accepted; handy for the demo.
                sesInboxUrl = aws.Enabled && aws.PublicServiceUrl is { } u ? $"{u.TrimEnd('/')}/_aws/ses" : null,
            });
        }).WithTags("Admin").RequireAuthorization("Admin");
        return app;
    }
}
