using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EverythingLabeled.Service.Tests;

public sealed class SqliteServiceFixture : IAsyncDisposable
{
    private SqliteServiceFixture(
        string directory,
        string dbPath,
        ElDbContext db,
        IUnitOfWork uow,
        ITagQueryCache cache)
    {
        Directory = directory;
        DbPath = dbPath;
        Db = db;
        Uow = uow;
        Cache = cache;
    }

    public string Directory { get; }
    public string DbPath { get; }
    public ElDbContext Db { get; }
    public IUnitOfWork Uow { get; }
    public ITagQueryCache Cache { get; }

    public TagService GetTagService() =>
        new(Db, Uow, new NameInitialsGenerator());

    public SuggestService GetSuggestService() =>
        new(Db, Cache);

    public ResourceService GetResourceService() =>
        new(Db, Uow);

    public ResourceTagService GetResourceTagService() =>
        new(Db, Uow, GetTagService());

    public RelationService GetRelationService() =>
        new(Db, Uow, GetTagService());

    public QueryService GetQueryService() =>
        new(
            Db,
            Cache,
            new QueryParser(),
            new QueryExpander(Db),
            new QueryExecutor(Db),
            new DbDataVersionReader(Db));

    public static async Task<SqliteServiceFixture> CreateAsync(ITagQueryCache? cache = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "el-svc-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "el.db");
        var cacheImpl = cache ?? new NullTagQueryCache();
        var db = OpenContext(dbPath);
        await DatabaseInitializer.InitializeAsync(db);
        var uow = new UnitOfWork(db, cacheImpl);
        return new SqliteServiceFixture(directory, dbPath, db, uow, cacheImpl);
    }

    public ElDbContext OpenNewContext() => OpenContext(DbPath);

    public static ElDbContext OpenContext(string dbPath, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ElDbContext>()
            .UseSqlite(DatabaseInitializer.BuildConnectionString(dbPath));
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        var options = builder.Options;
        var db = new ElDbContext(options);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        return db;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of temp SQLite files still briefly locked.
        }
    }
}
