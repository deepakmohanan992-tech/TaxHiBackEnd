using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ISearchRepository
{
    Task<IReadOnlyList<SelectedItems>> GetOutstationLocationsBySearchAsync(
        string searchWord,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableCabListing>> GetAvailableCabListingAsync(
        int? vendorId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int? locationFrom,
        int? locationTo,
        CancellationToken cancellationToken = default);
}
