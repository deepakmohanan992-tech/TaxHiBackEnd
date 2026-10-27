using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxHi.Application.Exceptions;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.FareQuotes;
using TaxHi.Application.Features.FareQuotes.Dtos;
using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.UnitTests;

public class FareQuoteServiceTests
{
    private sealed class FakeRouteProvider : IRouteProvider
    {
        public Task<RouteInfo?> GetRouteAsync(string pickup, string drop, CancellationToken cancellationToken = default) =>
            Task.FromResult<RouteInfo?>(null);
    }

    private sealed class FakeVehicleCatalog : IVehicleCatalog
    {
        private static readonly IReadOnlyList<VehicleCategory> Vehicles = new[]
        {
            new VehicleCategory("sedan", "Sedan", 4, 3, new[] { "AC" }),
        };

        public IReadOnlyList<VehicleCategory> GetAll() => Vehicles;
        public VehicleCategory? GetByCode(string code) =>
            Vehicles.FirstOrDefault(v => v.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class InMemoryFareQuoteStore : IFareQuoteStore
    {
        private readonly ConcurrentDictionary<Guid, FareQuote> _store = new();
        public Task SaveAsync(FareQuote quote, CancellationToken cancellationToken = default)
        {
            _store[quote.Id] = quote;
            return Task.CompletedTask;
        }
        public Task<FareQuote?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(quoteId, out var q);
            return Task.FromResult(q);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationException()
    {
        var options = Options.Create(new PricingOptions());
        var service = new FareQuoteService(
            new FakeRouteProvider(),
            new FakeVehicleCatalog(),
            new DefaultFareCalculator(options),
            new InMemoryFareQuoteStore(),
            options);

        var req = new FareQuoteRequest("Kochi", "Kochi", TripType.OneWay, DateTimeOffset.UtcNow.AddDays(1), null, "sedan", 2, null, null);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(req));
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsQuote()
    {
        var options = Options.Create(new PricingOptions());
        var service = new FareQuoteService(
            new FakeRouteProvider(),
            new FakeVehicleCatalog(),
            new DefaultFareCalculator(options),
            new InMemoryFareQuoteStore(),
            options);

        var req = new FareQuoteRequest("Kochi", "Coimbatore", TripType.OneWay, DateTimeOffset.UtcNow.AddDays(1), null, "sedan", 2, null, null);
        var resp = await service.CreateAsync(req);
        Assert.NotEqual(Guid.Empty, resp.QuoteId);
        Assert.True(resp.IsDevelopmentEstimate);
    }
}