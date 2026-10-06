using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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
        services.AddSingleton<QueryCachePipeline>();

        var bootstrap = new CacheOptions();
        configuration.GetSection(CacheOptions.SectionName).Bind(bootstrap);
        if (bootstrap.IsRedis())
        {
            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
                options.Validate();
                return RedisConnection.Connect(options);
            });
        }

        services.AddSingleton<ITagQueryCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
            options.Validate();
            var metrics = sp.GetRequiredService<QueryCacheMetrics>();
            if (options.IsMemory())
            {
                return new MemoryTagQueryCache(
                    options,
                    metrics,
                    sp.GetRequiredService<ILogger<MemoryTagQueryCache>>());
            }

            if (options.IsNull())
                return new NullTagQueryCache();

            var mux = sp.GetRequiredService<ConnectionMultiplexer>();
            return new RedisTagQueryCache(
                options,
                new StackExchangeRedisDatabase(mux.GetDatabase()),
                metrics,
                sp.GetRequiredService<ILogger<RedisTagQueryCache>>());
        });

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

