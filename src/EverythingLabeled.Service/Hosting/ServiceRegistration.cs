using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Hosting;

public static class ServiceRegistration
{
    public static IServiceCollection AddEverythingLabeledServices(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ITagQueryCache, NullTagQueryCache>();
        services.AddSingleton<INameInitialsGenerator, NameInitialsGenerator>();
        services.AddScoped<TagService>();
        services.AddScoped<ITagResolver>(sp => sp.GetRequiredService<TagService>());
        services.AddScoped<SuggestService>();
        services.AddScoped<ResourceService>();
        services.AddScoped<ResourceTagService>();
        return services;
    }
}
