using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class DbDataVersionReader(ElDbContext db) : IDataVersionReader
{
    public Task<int> GetAsync(CancellationToken cancellationToken) =>
        DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
}
