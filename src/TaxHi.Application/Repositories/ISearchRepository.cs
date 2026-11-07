using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ISearchRepository
{
    Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(
        LocationSearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(
        AvailableCabListingRequestDto request,
        CancellationToken cancellationToken = default);
}
