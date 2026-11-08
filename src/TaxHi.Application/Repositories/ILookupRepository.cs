using TaxHi.Application.Models;

namespace TaxHi.Application.Repositories;

public interface ILookupRepository
{
    Task<IReadOnlyList<SelectedItems>> GetVehicleTypesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SelectedItems>> GetSeatingCapacityAsync(
        CancellationToken cancellationToken = default);
}
