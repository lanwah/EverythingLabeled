using EverythingLabeled.Service.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(ElDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);
        await EnsureDataVersionAsync(db, cancellationToken);
    }

    public static async Task EnsureDataVersionAsync(ElDbContext db, CancellationToken cancellationToken = default)
    {
        var meta = await db.Meta.FindAsync([ElDbContext.DataVersionKey], cancellationToken);
        if (meta is null)
        {
            db.Meta.Add(new Meta { Key = ElDbContext.DataVersionKey, Value = "0" });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!int.TryParse(meta.Value, out _))
            throw new InvalidOperationException(
                $"Meta '{ElDbContext.DataVersionKey}' value '{meta.Value}' is not an integer.");
    }

    public static async Task<int> GetDataVersionAsync(ElDbContext db, CancellationToken cancellationToken = default)
    {
        var meta = await db.Meta.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Key == ElDbContext.DataVersionKey, cancellationToken);

        if (meta is null)
            throw new InvalidOperationException($"Meta '{ElDbContext.DataVersionKey}' is missing.");

        if (!int.TryParse(meta.Value, out var version))
            throw new InvalidOperationException(
                $"Meta '{ElDbContext.DataVersionKey}' value '{meta.Value}' is not an integer.");

        return version;
    }

    public static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
}
