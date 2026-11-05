using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HomeShowing.Listings;

public static class ListingsModule
{
    public static IServiceCollection AddListingsModule(this IServiceCollection services)
    {
        services.AddScoped<ListingRepository>();
        services.AddSingleton<IPhotoStorage>(sp => sp.GetRequiredService<BuildingBlocks.Aws.AwsOptions>().Enabled
            ? new S3PhotoStorage(sp.GetRequiredService<BuildingBlocks.Aws.AwsOptions>()) : new DisabledPhotoStorage());
        services.AddScoped(sp => new ListingService(
            sp.GetRequiredService<ListingRepository>(),
            sp.GetRequiredService<BuildingBlocks.Tenancy.ITenantContext>(),
            sp.GetRequiredService<NodaTime.IClock>(),
            sp.GetRequiredService<BuildingBlocks.Audit.IAuditLog>(),
            sp.GetRequiredService<IPhotoStorage>()));
        services.AddScoped<IListingCatalog>(sp => sp.GetRequiredService<ListingService>());
        services.AddSingleton<IMongoSchemaSetup, ListingSchema>();
        return services;
    }
}
