using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Worker;
using NodaTime;

var builder = Host.CreateApplicationBuilder(args);
var mongo = new MongoOptions();
builder.Configuration.GetSection("Mongo").Bind(mongo);
builder.Services.AddSingleton(mongo);
builder.Services.AddSingleton<MongoDb>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddSingleton<OutboxProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor>());
builder.Build().Run();
