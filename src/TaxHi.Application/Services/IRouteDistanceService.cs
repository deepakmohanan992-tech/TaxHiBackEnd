using TaxHi.Application.Models;

namespace TaxHi.Application.Services;

public interface IRouteDistanceService
{
    /// <summary>
    /// Distance in km between the request's pickup and drop coordinates: Google road distance, or the straight-line
    /// distance when Google is unavailable. 0 when either coordinate pair is missing / invalid or both points coincide.
    /// </summary>
    Task<decimal> CalculateKmAsync(AvailableCabListingRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Road distance (Google, else straight line) between two coordinate pairs; null when either pair is missing or invalid.
    /// Identical points give 0, which is a real distance.
    /// </summary>
    Task<decimal?> CalculateKmBetweenAsync(decimal? fromLatitude, decimal? fromLongitude, decimal? toLatitude, decimal? toLongitude, CancellationToken cancellationToken = default);
}
