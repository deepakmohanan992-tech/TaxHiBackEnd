using TaxHi.Application.Models;

namespace TaxHi.Application.Services;

public interface ITripContextService
{
    /// <summary>
    /// Works out the rate algorithm and, for Airport mode, the airport terminal and direction from the request's
    /// locations: a terminal of the vendor's sector matching the pickup (direction Pickup) or the drop (direction Drop),
    /// by Google place id or by coordinates. Returns null for requests that are not a trip mode (no mode, or Local).
    /// </summary>
    Task<TripContextResult?> ResolveAsync(AvailableCabListingRequestDto request, decimal journeyKm, CancellationToken cancellationToken = default);
}
