using Microsoft.Extensions.Logging;
using TaxHi.Application.Models;
using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Services;

public sealed class RouteDistanceService(IGoogleMapsService mapsService, ILogger<RouteDistanceService> logger) : IRouteDistanceService
{
    private const double EarthRadiusKm = 6371.0088;

    public async Task<decimal> CalculateKmAsync(AvailableCabListingRequestDto request, CancellationToken cancellationToken = default) =>
        await CalculateKmBetweenAsync(request.PickupLatitude, request.PickupLongitude, request.DropLatitude, request.DropLongitude, cancellationToken) ?? 0m;

    public async Task<decimal?> CalculateKmBetweenAsync(
        decimal? fromLatitude, decimal? fromLongitude, decimal? toLatitude, decimal? toLongitude,
        CancellationToken cancellationToken = default)
    {
        if (!IsUsable(fromLatitude, fromLongitude) || !IsUsable(toLatitude, toLongitude))
            return null;

        var (pLat, pLng, dLat, dLng) = ((double)fromLatitude!.Value, (double)fromLongitude!.Value,
                                        (double)toLatitude!.Value, (double)toLongitude!.Value);
        if (pLat == dLat && pLng == dLng)
            return 0m;

        var road = await mapsService.GetRoadDistanceKmAsync(pLat, pLng, dLat, dLng, cancellationToken);
        if (road.Success && road.Data > 0)
            return Math.Round(road.Data, 2);

        logger.LogWarning("Road distance unavailable ({ErrorCode}); using straight-line distance.", road.ErrorCode);
        return Math.Round((decimal)Haversine(pLat, pLng, dLat, dLng), 2);
    }

    private static bool IsUsable(decimal? lat, decimal? lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);

    internal static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
