using HomeShowing.BuildingBlocks.Audit;
using HomeShowing.BuildingBlocks.Events;
using HomeShowing.BuildingBlocks.Outbox;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;

namespace HomeShowing.BuildingBlocks;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services, IConfiguration config)
    {
        var mongo = new MongoOptions();
        config.GetSection("Mongo").Bind(mongo);
        services.AddSingleton(mongo);
        var aws = new Aws.AwsOptions();
        config.GetSection("Aws").Bind(aws);
        services.AddSingleton(aws);
        services.AddSingleton<MongoDb>();
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.AddScoped<MongoSessionScope>();
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IOutbox, MongoOutbox>();
        services.AddScoped<IAuditLog, MongoAuditLog>();
        services.AddSingleton<IMongoSchemaSetup, OutboxSchema>();
        return services;
    }

    public static async Task ApplyMongoSchemaAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<MongoDb>();
        foreach (var setup in sp.GetServices<IMongoSchemaSetup>())
            await setup.ApplyAsync(db, ct);
    }
}
