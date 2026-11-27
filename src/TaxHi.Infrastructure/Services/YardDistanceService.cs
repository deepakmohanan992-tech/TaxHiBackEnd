using Microsoft.Extensions.Logging;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Services;

public sealed class YardDistanceService(
    ISearchRepository searchRepository,
    IRouteDistanceService routeDistanceService,
    ILogger<YardDistanceService> logger) : IYardDistanceService
{
    private const int MaxParallelLookups = 4;

    public async Task<IReadOnlyList<YardDistance>> GetYardDistancesAsync(
        AvailableCabListingRequestDto request, TripContext trip, CancellationToken cancellationToken = default)
    {
        var yards = await searchRepository.GetVendorYardsAsync(request.VendorId, cancellationToken);
        if (yards.Count == 0)
        {
            logger.LogWarning("No active yard found for vendor {VendorId}.", request.VendorId);
            return [];
        }

        using var gate = new SemaphoreSlim(MaxParallelLookups);

        // Only yard -> pickup is measured. Yards sharing a location need one lookup, not one each.
        var byLocation = yards
            .GroupBy(y => (y.Latitude, y.Longitude))
            .ToDictionary(g => g.Key, g => LookupToPickupAsync(g.Key.Latitude, g.Key.Longitude));

        await Task.WhenAll(byLocation.Values);

        return yards
            .Select(y =>
            {
                var toPickup = byLocation[(y.Latitude, y.Longitude)].Result;
                if (toPickup is null)
                    logger.LogWarning("Yard {YardId} has no usable distance to the pickup (coordinates {Lat},{Lng}).", y.YardId, y.Latitude, y.Longitude);
                return new YardDistance(y.YardId, toPickup);
            })
            .ToList();

        async Task<decimal?> LookupToPickupAsync(decimal? latitude, decimal? longitude)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await routeDistanceService.CalculateKmBetweenAsync(
                    latitude, longitude, request.PickupLatitude, request.PickupLongitude, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
