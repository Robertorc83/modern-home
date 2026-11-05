using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SQS;
using HomeShowing.BuildingBlocks.Aws;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Worker;
using NodaTime;

var builder = Host.CreateApplicationBuilder(args);
var mongo = new MongoOptions();
builder.Configuration.GetSection("Mongo").Bind(mongo);
var aws = new AwsOptions();
builder.Configuration.GetSection("Aws").Bind(aws);

builder.Services.AddSingleton(mongo);
builder.Services.AddSingleton(aws);
builder.Services.AddSingleton<MongoDb>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

if (aws.Enabled)
{
    // Outbox -> SQS -> consumer -> SES. ServiceUrl points at LocalStack locally; empty in real AWS.
    var region = Amazon.RegionEndpoint.GetBySystemName(aws.Region);
    var credentials = FallbackCredentialsFactory.GetCredentials();
    builder.Services.AddSingleton<IAmazonSQS>(new AmazonSQSClient(credentials, new AmazonSQSConfig { RegionEndpoint = region, ServiceURL = aws.ServiceUrl }));
    builder.Services.AddSingleton<IAmazonSimpleEmailService>(new AmazonSimpleEmailServiceClient(credentials, new AmazonSimpleEmailServiceConfig { RegionEndpoint = region, ServiceURL = aws.ServiceUrl }));
    builder.Services.AddSingleton<IEmailSender, SesEmailSender>();
    builder.Services.AddSingleton(sp => new OutboxProcessor(sp.GetRequiredService<MongoDb>(), sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<OutboxProcessor>>(), direct: false));
    builder.Services.AddHostedService<SqsOutboxRelay>();
    builder.Services.AddHostedService<SqsConsumer>();
}
else
{
    // No AWS: deliver straight from the outbox and log the email.
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
    builder.Services.AddSingleton(sp => new OutboxProcessor(sp.GetRequiredService<MongoDb>(), sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<OutboxProcessor>>()));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor>());
}

builder.Build().Run();
