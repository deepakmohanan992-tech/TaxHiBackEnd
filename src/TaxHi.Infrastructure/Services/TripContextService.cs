using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Services;

public sealed class TripContextService(ISearchRepository searchRepository) : ITripContextService
{
    /// <summary>A pickup/drop this close to a terminal's coordinates (km) is that terminal.</summary>
    internal const double TerminalMatchRadiusKm = 1.0;

    public async Task<TripContextResult?> ResolveAsync(
        AvailableCabListingRequestDto request, decimal journeyKm, CancellationToken cancellationToken = default)
    {
        var mode = TripModes.Normalize(request.Mode);
        if (!TripModes.UsesYardDistances(mode))
            return null;

        var algorithm = ResolveRateAlgorithm(request, journeyKm);
        if (mode != TripModes.Airport)
            return new TripContextResult(new TripContext(mode!, algorithm));

        if (request.VendorId is null or <= 0)
            return new TripContextResult(null, "VENDOR_REQUIRED", "VendorId is required for Airport mode.");

        var terminals = await searchRepository.GetAirportTerminalsAsync(
            request.VendorId.Value, new PaginationRequest { CurrentPage = 1, PageSize = 100 }, cancellationToken);

        // The pickup is tried first, so an airport-to-airport trip is treated as a pickup at the first airport.
        var pickup = FindTerminal(terminals.Items, request.PickupPlaceId, request.PickupLatitude, request.PickupLongitude);
        if (pickup is not null)
            return new TripContextResult(new TripContext(mode!, algorithm, pickup.Id, TripModes.AirportPickup));

        var drop = FindTerminal(terminals.Items, request.DropPlaceId, request.DropLatitude, request.DropLongitude);
        if (drop is not null)
            return new TripContextResult(new TripContext(mode!, algorithm, drop.Id, TripModes.AirportDrop));

        return new TripContextResult(null, "AIRPORT_NOT_FOUND", "Neither the pickup nor the drop is an airport terminal of this vendor.");
    }

    /// <summary>
    /// Rate algorithm (100, 150 or 200) of the trip. The business rule that chooses between them is not defined yet,
    /// so every trip uses 100 until it is; this is the single place to add it.
    /// </summary>
    internal static int ResolveRateAlgorithm(AvailableCabListingRequestDto request, decimal journeyKm) => 100;

    internal static AirportTerminalItem? FindTerminal(IEnumerable<AirportTerminalItem> terminals, string? placeId, decimal? latitude, decimal? longitude)
    {
        var list = terminals as IReadOnlyCollection<AirportTerminalItem> ?? terminals.ToList();

        if (!string.IsNullOrWhiteSpace(placeId))
        {
            var byPlace = list.FirstOrDefault(t => string.Equals(t.PlaceId, placeId.Trim(), StringComparison.Ordinal));
            if (byPlace is not null) return byPlace;
        }

        if (latitude is null || longitude is null) return null;

        return list
            .Where(t => t.Latitude is not null && t.Longitude is not null)
            .Select(t => (Terminal: t, Km: DistanceKm((double)latitude, (double)longitude, (double)t.Latitude!, (double)t.Longitude!)))
            .Where(x => x.Km <= TerminalMatchRadiusKm)
            .OrderBy(x => x.Km)
            .Select(x => x.Terminal)
            .FirstOrDefault();
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2) =>
        RouteDistanceService.Haversine(lat1, lon1, lat2, lon2);
}
