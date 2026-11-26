using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ISearchRepository
{
    Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(
        LocationSearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(
        AvailableCabListingRequestDto request,
        decimal km,
        CancellationToken cancellationToken = default);

    /// <summary>Active airport terminals of the sector the vendor belongs to, popular first, searchable and paginated.</summary>
    Task<PagedResult<AirportTerminalItem>> GetAirportTerminalsAsync(
        int vendorId,
        PaginationRequest request,
        CancellationToken cancellationToken = default);
}
