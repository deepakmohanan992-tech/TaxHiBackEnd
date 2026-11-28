using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Services;

namespace TaxHi.UnitTests;

public class VendorConfigurationTests
{
    private static BookingService Create(FakeRepo repo) =>
        new(repo, null!, null!, null!, null!, Options.Create(new AuthenticationOptions { JwtSigningKey = new string('k', 40) }),
            null!, new ConfigurationBuilder().Build(), NullLogger<BookingService>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task Rejects_non_positive_vendor(int vendorId)
    {
        var repo = new FakeRepo();
        var response = await Create(repo).GetVendorConfigurationAsync(vendorId, null);
        Assert.False(response.Success);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("INVALID_VENDOR_ID", response.ErrorCode);
        Assert.False(repo.Called);
    }

    [Fact]
    public async Task Rejects_non_positive_brand()
    {
        var response = await Create(new FakeRepo()).GetVendorConfigurationAsync(1, 0);
        Assert.Equal("INVALID_VEHICLE_BRAND_ID", response.ErrorCode);
    }

    [Fact]
    public async Task Splits_items_by_type_keeping_order_and_dropping_duplicates()
    {
        var repo = new FakeRepo
        {
            Config = new VendorConfig { ReturnTripRatePerc = 10, IsPartialPaymentAllowed = true, PartialPaymentPerc = 25 },
            Items =
            [
                new() { ItemType = 1, ItemText = "Driver Allowance" },
                new() { ItemType = 1, ItemText = " driver allowance " },
                new() { ItemType = 1, ItemText = "Fuel Charges" },
                new() { ItemType = 2, ItemText = "Toll Charges" },
                new() { ItemType = 2, ItemText = "  " },
                new() { ItemType = 2, ItemText = "Parking Charges" }
            ]
        };

        var response = await Create(repo).GetVendorConfigurationAsync(7, 3);

        Assert.True(response.Success);
        Assert.Equal((7, 3), (repo.VendorId, repo.BrandId));
        Assert.Equal(10m, response.Data!.ReturnTripRatePerc);
        Assert.True(response.Data.IsPartialPaymentAllowed);
        Assert.Equal(25m, response.Data.PartialPaymentPerc);
        Assert.Equal(["Driver Allowance", "Fuel Charges"], response.Data.Inclusions);
        Assert.Equal(["Toll Charges", "Parking Charges"], response.Data.Exclusions);
    }

    [Fact]
    public async Task Missing_config_and_items_give_safe_defaults()
    {
        var response = await Create(new FakeRepo()).GetVendorConfigurationAsync(7, null);

        Assert.True(response.Success);
        Assert.Equal(0m, response.Data!.ReturnTripRatePerc);
        Assert.False(response.Data.IsPartialPaymentAllowed);
        Assert.Equal(0m, response.Data.PartialPaymentPerc);
        Assert.Empty(response.Data.Inclusions);
        Assert.Empty(response.Data.Exclusions);
    }

    private sealed class FakeRepo : IBookingRepository
    {
        public VendorConfig? Config;
        public List<VendorItem> Items = [];
        public bool Called;
        public int? VendorId;
        public int? BrandId;

        public Task<(VendorConfig? Config, IReadOnlyList<VendorItem> Items)> GetVendorConfigurationAsync(int? vendorId, int? vehicleBrandId, CancellationToken cancellationToken = default)
        {
            Called = true;
            VendorId = vendorId;
            BrandId = vehicleBrandId;
            return Task.FromResult((Config, (IReadOnlyList<VendorItem>)Items));
        }

        public Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(SaveBookingPaymentRequest request, int userId, BookingAmounts amounts, string bookingNo, string tripCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingListResponseDto> GetBookingsAsync(int userId, string mode, int currentPage, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingPaymentContext?> GetPaymentContextAsync(long? bookingId, string? gatewayOrderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> SetGatewayOrderAsync(long bookingPaymentId, string gatewayOrderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> ConfirmGatewayPaymentAsync(string gatewayOrderId, string gatewayPaymentId, long? amountPaise, string? paymentMethod, string? eventId, string? eventType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingCancellationContext?> GetCancellationContextAsync(long bookingId, long userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingCancellationResult> CancelBookingAsync(long bookingId, long userId, long? tripId, string reason, string? remarks, decimal refundAmount, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> CancelPaymentAttemptAsync(long bookingId, long userId, string? reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ExpireLapsedHoldsAsync(int graceMinutes, int batchSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> FailGatewayPaymentAsync(string gatewayOrderId, string? gatewayPaymentId, string? reason, string? eventId, string? eventType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
