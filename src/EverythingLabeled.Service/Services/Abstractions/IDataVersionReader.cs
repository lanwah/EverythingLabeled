namespace EverythingLabeled.Service.Services.Abstractions;

public interface IDataVersionReader
{
    Task<int> GetAsync(CancellationToken cancellationToken);
}
