using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Hosting;

public static class ServiceRegistration
{
    public static IServiceCollection AddEverythingLabeledServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName));
        services.AddSingleton<QueryCacheMetrics>();
        services.AddSingleton<QueryCacheSingleflight>();
        services.AddSingleton<ITagQueryCache, NullTagQueryCache>();
        services.AddSingleton<QueryCachePipeline>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<INameInitialsGenerator, NameInitialsGenerator>();
        services.AddScoped<IDataVersionReader, DbDataVersionReader>();
        services.AddScoped<TagService>();
        services.AddScoped<ITagResolver>(sp => sp.GetRequiredService<TagService>());
        services.AddScoped<SuggestService>();
        services.AddScoped<ResourceService>();
        services.AddScoped<ResourceTagService>();
        services.AddScoped<RelationService>();
        services.AddSingleton<QueryParser>();
        services.AddScoped<QueryExpander>();
        services.AddScoped<QueryExecutor>();
        services.AddScoped<QueryService>();
        return services;
    }
}

