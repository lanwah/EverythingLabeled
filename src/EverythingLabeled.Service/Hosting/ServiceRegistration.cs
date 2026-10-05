using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Hosting;

public static class ServiceRegistration
{
    public static IServiceCollection AddEverythingLabeledServices(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ITagQueryCache, NullTagQueryCache>();
        services.AddSingleton<INameInitialsGenerator, NameInitialsGenerator>();
        return services;
    }
}
