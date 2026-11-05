using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.Scheduling.Application;
using HomeShowing.Scheduling.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HomeShowing.Scheduling;

public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        services.AddScoped<ShowingRepository>();
        services.AddScoped<SlotHoldRepository>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<BookingService>();
        services.AddScoped<ShowingChangeFeed>();
        services.AddSingleton<IMongoSchemaSetup, SchedulingSchema>();
        return services;
    }
}
