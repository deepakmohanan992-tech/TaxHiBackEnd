using Microsoft.Extensions.Logging.Abstractions;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Services;

namespace TaxHi.UnitTests;

public class AirportTerminalServiceTests
{
    [Theory]
    [InlineData(null, 1, 10, "INVALID_VENDOR_ID")]
    [InlineData(0, 1, 10, "INVALID_VENDOR_ID")]
    [InlineData(3, 0, 10, "INVALID_CURRENT_PAGE")]
    [InlineData(3, 1, 0, "INVALID_PAGE_SIZE")]
    [InlineData(3, 1, 101, "INVALID_PAGE_SIZE")]
    public async Task Invalid_input_is_rejected_before_the_repository_is_called(int? vendorId, int page, int size, string code)
    {
        var repo = new FakeSearchRepo();
        var service = new AirportTerminalService(repo, NullLogger<AirportTerminalService>.Instance);

        var response = await service.GetAirportTerminalsAsync(vendorId, new PaginationRequest { CurrentPage = page, PageSize = size });

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(code, response.ErrorCode);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Valid_request_returns_the_paged_terminals_for_the_vendor()
    {
        var repo = new FakeSearchRepo();
        var service = new AirportTerminalService(repo, NullLogger<AirportTerminalService>.Instance);

        var response = await service.GetAirportTerminalsAsync(3, new PaginationRequest { CurrentPage = 2, PageSize = 5, Keyword = "cok" });

        Assert.True(response.Success);
        Assert.Equal(3, repo.VendorId);
        Assert.Equal("cok", repo.Keyword);
        Assert.Single(response.Data!.Items);
    }

    private sealed class FakeSearchRepo : ISearchRepository
    {
        public int Calls, VendorId; public string? Keyword;

        public Task<PagedResult<AirportTerminalItem>> GetAirportTerminalsAsync(int vendorId, PaginationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; VendorId = vendorId; Keyword = request.Keyword;
            return Task.FromResult(new PagedResult<AirportTerminalItem>
            {
                Items = [new AirportTerminalItem { Id = 1, Value = "Cochin", Latitude = 10.15m, Longitude = 76.39m, PlaceId = "ChIJabc" }],
                CurrentPage = request.CurrentPage, PageSize = request.PageSize, TotalItems = 1, TotalPages = 1
            });
        }

        public Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(LocationSearchRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(AvailableCabListingRequestDto request, decimal km, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
