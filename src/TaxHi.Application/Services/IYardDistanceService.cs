using TaxHi.Application.Models;

namespace TaxHi.Application.Services;

public interface IYardDistanceService
{
    /// <summary>
    /// Road distance of every active yard of the searched vendor(s) to the pickup location only (the drop is never measured).
    /// Uses the same Google road-distance mechanism as the customer journey. A distance that cannot be resolved
    /// (yard without coordinates, invalid coordinates) comes back as null, never 0.
    /// </summary>
    Task<IReadOnlyList<YardDistance>> GetYardDistancesAsync(
        AvailableCabListingRequestDto request, TripContext trip, CancellationToken cancellationToken = default);
}
