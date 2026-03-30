using Petrichor.Core.Domain;

namespace Petrichor.Core.Abstractions;

public interface IFolderRepository
{
    Task<IReadOnlyList<FolderReference>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FolderReference?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
    Task UpsertAsync(FolderReference folder, CancellationToken cancellationToken = default);
    Task DeleteByPathAsync(string path, CancellationToken cancellationToken = default);
}
