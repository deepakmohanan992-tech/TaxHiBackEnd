using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ILookupRepository
{
    Task<IReadOnlyList<SelectedItems>> GetVehicleTypesAsync(
        int? vendorId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SelectedItems>> GetSeatingCapacityAsync(
        int? vendorId = null,
        CancellationToken cancellationToken = default);
}
