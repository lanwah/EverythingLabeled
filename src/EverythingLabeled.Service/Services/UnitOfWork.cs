using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EverythingLabeled.Service.Services;

public sealed class UnitOfWork(ElDbContext db, ITagQueryCache cache) : IUnitOfWork
{
    private bool _dirty;

    public void MarkDirty() => _dirty = true;

    public async Task<int> CommitAsync(CancellationToken cancellationToken)
    {
        if (!_dirty)
        {
            await db.SaveChangesAsync(cancellationToken);
            return await DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = await DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Meta SET Value = CAST(Value AS INTEGER) + 1 WHERE Key = {ElDbContext.DataVersionKey}",
            cancellationToken);
        var next = await DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        _dirty = false;

        var prefix = $"v{previous}:";
        _ = InvalidateQuietlyAsync(prefix);

        return next;
    }

    private async Task InvalidateQuietlyAsync(string prefix)
    {
        try
        {
            await cache.InvalidatePrefixAsync(prefix, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cache prefix invalidation failed for {Prefix}", prefix);
        }
    }
}
