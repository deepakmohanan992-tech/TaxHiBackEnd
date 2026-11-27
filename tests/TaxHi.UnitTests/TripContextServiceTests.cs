using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Services;

namespace TaxHi.UnitTests;

public class TripContextServiceTests
{
    private static readonly AirportTerminalItem Cochin = new() { Id = 5, Value = "Cochin T3", Latitude = 10.1520m, Longitude = 76.4019m, PlaceId = "PLACE_COK" };
    private static readonly AirportTerminalItem Trivandrum = new() { Id = 9, Value = "Trivandrum", Latitude = 8.4821m, Longitude = 76.9201m, PlaceId = "PLACE_TRV" };

    private static TripContextService Create() => new(new FakeRepo([Cochin, Trivandrum]));

    private static AvailableCabListingRequestDto Request(string? mode, string? pickupPlace = null, decimal? pickupLat = 9.99m, decimal? pickupLng = 76.30m,
        string? dropPlace = null, decimal? dropLat = 10.5m, decimal? dropLng = 76.6m, int? vendor = 3) => new()
    {
        VendorId = vendor, Mode = mode, PickupPlaceId = pickupPlace, PickupLatitude = pickupLat, PickupLongitude = pickupLng,
        DropPlaceId = dropPlace, DropLatitude = dropLat, DropLongitude = dropLng
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Local")]
    public async Task Non_trip_modes_need_no_context(string? mode) =>
        Assert.Null(await Create().ResolveAsync(Request(mode), 50m));

    [Theory]
    [InlineData("Oneway")]
    [InlineData("roundtrip")]
    public async Task Oneway_and_round_trip_get_the_algorithm_only(string mode)
    {
        var result = await Create().ResolveAsync(Request(mode), 50m);

        Assert.True(result!.Succeeded);
        Assert.Equal(100, result.Context!.RateAlgorithm);
        Assert.Null(result.Context.AirportTerminalId);
        Assert.Null(result.Context.AirportDirection);
    }

    [Fact]
    public async Task Pickup_at_a_terminal_by_place_id_is_an_airport_pickup()
    {
        var result = await Create().ResolveAsync(Request("Airport", pickupPlace: "PLACE_COK"), 50m);

        Assert.Equal(new TripContext("Airport", 100, 5, "Pickup"), result!.Context);
    }

    [Fact]
    public async Task Drop_near_a_terminal_by_coordinates_is_an_airport_drop()
    {
        var result = await Create().ResolveAsync(Request("Airport", dropLat: 10.1531m, dropLng: 76.4010m), 50m);

        Assert.Equal(new TripContext("Airport", 100, 5, "Drop"), result!.Context);
    }

    [Fact]
    public async Task Airport_to_airport_is_treated_as_a_pickup_at_the_first_airport()
    {
        var result = await Create().ResolveAsync(Request("Airport", pickupPlace: "PLACE_TRV", dropPlace: "PLACE_COK"), 200m);

        Assert.Equal(new TripContext("Airport", 100, 9, "Pickup"), result!.Context);
    }

    [Fact]
    public async Task Airport_mode_without_a_terminal_in_the_trip_is_rejected()
    {
        var result = await Create().ResolveAsync(Request("Airport"), 50m);

        Assert.False(result!.Succeeded);
        Assert.Equal("AIRPORT_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Airport_mode_without_a_vendor_is_rejected()
    {
        var result = await Create().ResolveAsync(Request("Airport", pickupPlace: "PLACE_COK", vendor: null), 50m);

        Assert.Equal("VENDOR_REQUIRED", result!.ErrorCode);
    }

    private sealed class FakeRepo(AirportTerminalItem[] terminals) : ISearchRepository
    {
        public Task<PagedResult<AirportTerminalItem>> GetAirportTerminalsAsync(int vendorId, PaginationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AirportTerminalItem> { Items = terminals, CurrentPage = 1, PageSize = request.PageSize, TotalItems = terminals.Length, TotalPages = 1 });

        public Task<IReadOnlyList<VendorYard>> GetVendorYardsAsync(int? vendorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(LocationSearchRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(AvailableCabListingRequestDto request, decimal km, TripContext? trip, IReadOnlyList<YardDistance>? yardDistances, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
