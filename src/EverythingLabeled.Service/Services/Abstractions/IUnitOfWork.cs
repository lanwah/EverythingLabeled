namespace EverythingLabeled.Service.Services.Abstractions;

public interface IUnitOfWork
{
    void MarkDirty();
    Task<int> CommitAsync(CancellationToken cancellationToken);
}
