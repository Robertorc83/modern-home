using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using HomeShowing.BuildingBlocks.Aws;
using HomeShowing.Notifications;
using System.Text.Json;

namespace HomeShowing.Worker;

/// <summary>Amazon SES behind the same interface as the dev logger. Sandbox or LocalStack: the sender must be verified.</summary>
public sealed class SesEmailSender(IAmazonSimpleEmailService ses, AwsOptions options, ILogger<SesEmailSender> log) : IEmailSender
{
    public string Transport => "ses";

    public async Task SendAsync(EmailIntent email, CancellationToken ct)
    {
        var res = await ses.SendEmailAsync(new SendEmailRequest
        {
            Source = options.SenderEmail,
            Destination = new Destination { ToAddresses = [email.To] },
            Message = new Amazon.SimpleEmail.Model.Message
            {
                Subject = new Content(email.Subject),
                Body = new Body { Text = new Content(email.Body + "\n\n— Home Showing Scheduler") },
            },
        }, ct);
        log.LogInformation("SES message {MessageId} to {To}: {Subject}", res.MessageId, email.To, email.Subject);
    }
}

/// <summary>
/// Outbox relay: moves committed outbox rows onto SQS. The queue decouples the delivery rate from the database and
/// gives retries, visibility timeouts and a dead letter queue for free. The consumer below drains it.
/// </summary>
public sealed class SqsOutboxRelay(IAmazonSQS sqs, AwsOptions options, OutboxProcessor outbox, ILogger<SqsOutboxRelay> log) : BackgroundService
{
    private string? _queueUrl;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _queueUrl = await ResolveQueueAsync(stoppingToken);
        log.LogInformation("Outbox relay started: outbox -> {Queue}", options.NotificationsQueue);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var relayed = await outbox.RelayNextAsync(async (msg, ct) =>
                {
                    await sqs.SendMessageAsync(new SendMessageRequest
                    {
                        QueueUrl = _queueUrl,
                        MessageBody = JsonSerializer.Serialize(new QueueEnvelope(msg.Id, msg.TenantId, msg.Type)),

                    }, ct);
                }, stoppingToken);
                if (!relayed) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox relay error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<string> ResolveQueueAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return (await sqs.GetQueueUrlAsync(options.NotificationsQueue, ct)).QueueUrl; }
            catch (Exception ex) when (attempt < 30)
            {
                log.LogWarning("Queue {Queue} not ready ({Error}); retrying", options.NotificationsQueue, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}

public sealed record QueueEnvelope(string OutboxId, string TenantId, string Type);

/// <summary>Consumes the SQS queue and delivers each outbox row. Idempotent: a message redelivered after a crash
/// finds the <c>sentEmails</c> record (unique on outbox id) and is simply acknowledged.</summary>
public sealed class SqsConsumer(IAmazonSQS sqs, AwsOptions options, OutboxProcessor outbox, ILogger<SqsConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string queueUrl;
        while (true)
        {
            try { queueUrl = (await sqs.GetQueueUrlAsync(options.NotificationsQueue, stoppingToken)).QueueUrl; break; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
        }
        log.LogInformation("SQS consumer started on {Queue}", options.NotificationsQueue);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 10 }, stoppingToken);
                foreach (var m in batch.Messages ?? [])
                {
                    var env = JsonSerializer.Deserialize<QueueEnvelope>(m.Body);
                    if (env is null) { await sqs.DeleteMessageAsync(queueUrl, m.ReceiptHandle, stoppingToken); continue; }
                    var done = await outbox.DeliverAsync(env.OutboxId, stoppingToken);
                    if (done) await sqs.DeleteMessageAsync(queueUrl, m.ReceiptHandle, stoppingToken);
                    // Not done: leave it; SQS redelivers after the visibility timeout and the DLQ catches repeat failures.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "SQS consumer error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
