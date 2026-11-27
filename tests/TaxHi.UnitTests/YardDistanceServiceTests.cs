using Microsoft.Extensions.Logging.Abstractions;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Services;

namespace TaxHi.UnitTests;

public class YardDistanceServiceTests
{
    private static AvailableCabListingRequestDto Request(string mode, string? direction = null) => new()
    {
        VendorId = 1, Mode = mode,
        PickupLatitude = 9.99m, PickupLongitude = 76.30m, DropLatitude = 10.50m, DropLongitude = 76.60m
    };

    private static TripContext Trip(string mode, string? direction = null) => new(mode, 100, mode == "Airport" ? 5 : null, direction);

    private static YardDistanceService Create(FakeRoute route, params VendorYard[] yards) =>
        new(new FakeRepo(yards), route, NullLogger<YardDistanceService>.Instance);

    [Theory]
    [InlineData("Oneway", null)]
    [InlineData("RoundTrip", null)]
    [InlineData("Airport", "Pickup")]
    [InlineData("Airport", "Drop")]
    public async Task Only_yard_to_pickup_is_measured_in_every_mode(string mode, string? direction)
    {
        var route = new FakeRoute();
        var result = await Create(route, new VendorYard { YardId = 1, Latitude = 9.9m, Longitude = 76.2m })
            .GetYardDistancesAsync(Request(mode, direction), Trip(mode, direction));

        Assert.Equal(new YardDistance(1, 10m), Assert.Single(result));
        Assert.Equal(1, route.Calls);
        Assert.All(route.Lookups, l => Assert.Equal((9.99m, 76.30m), l.To)); // always the pickup, never the drop
    }

    [Fact]
    public async Task Yard_without_coordinates_is_reported_unresolved_not_zero()
    {
        var result = await Create(new FakeRoute(), new VendorYard { YardId = 7 }).GetYardDistancesAsync(Request("Oneway"), Trip("Oneway"));

        var yard = Assert.Single(result);
        Assert.Null(yard.ToPickupKm);
    }

    [Fact]
    public async Task No_yards_gives_an_empty_list()
    {
        Assert.Empty(await Create(new FakeRoute()).GetYardDistancesAsync(Request("Oneway"), Trip("Oneway")));
    }

    [Fact]
    public async Task Yards_at_the_same_location_share_one_lookup_and_each_keeps_its_id()
    {
        var route = new FakeRoute();
        var result = await Create(route,
                new VendorYard { YardId = 1, Latitude = 9.9m, Longitude = 76.2m },
                new VendorYard { YardId = 2, Latitude = 9.9m, Longitude = 76.2m },
                new VendorYard { YardId = 3, Latitude = 9.8m, Longitude = 76.1m })
            .GetYardDistancesAsync(Request("Oneway"), Trip("Oneway"));

        Assert.Equal([1, 2, 3], result.Select(y => y.YardId));
        Assert.Equal(2, route.Calls); // one yard -> pickup lookup per distinct location
    }

    private sealed class FakeRepo(VendorYard[] yards) : ISearchRepository
    {
        public Task<IReadOnlyList<VendorYard>> GetVendorYardsAsync(int? vendorId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VendorYard>>(yards);
        public Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(LocationSearchRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(AvailableCabListingRequestDto request, decimal km, TripContext? trip, IReadOnlyList<YardDistance>? yardDistances, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<AirportTerminalItem>> GetAirportTerminalsAsync(int vendorId, PaginationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    // Pickup is the point (9.99, 76.30): any yard is 10 km from it; any other pair is 40 km.
    private sealed class FakeRoute : IRouteDistanceService
    {
        public int Calls;
        public List<((decimal? Lat, decimal? Lng) From, (decimal? Lat, decimal? Lng) To)> Lookups = [];

        public Task<decimal> CalculateKmAsync(AvailableCabListingRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<decimal?> CalculateKmBetweenAsync(decimal? fromLatitude, decimal? fromLongitude, decimal? toLatitude, decimal? toLongitude, CancellationToken cancellationToken = default)
        {
            lock (Lookups) Lookups.Add(((fromLatitude, fromLongitude), (toLatitude, toLongitude)));
            Interlocked.Increment(ref Calls);
            if (fromLatitude is null || toLatitude is null) return Task.FromResult<decimal?>(null);
            var touchesPickup = fromLatitude == 9.99m || toLatitude == 9.99m;
            return Task.FromResult<decimal?>(touchesPickup ? 10m : 40m);
        }
    }
}
