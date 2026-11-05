using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ISearchRepository
{
    Task<IReadOnlyList<SelectedItems>> GetOutstationLocationsBySearchAsync(
        string searchWord,
        CancellationToken cancellationToken = default);
}
