using TaxHi.Application.Exceptions;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.FareQuotes.Dtos;
using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.FareQuotes;

public sealed class FareQuoteService
{
    private readonly IRouteProvider _routeProvider;
    private readonly IVehicleCatalog _vehicleCatalog;
    private readonly IFareCalculator _fareCalculator;
    private readonly IFareQuoteStore _quoteStore;
    private readonly PricingOptions _pricing;

    public FareQuoteService(
        IRouteProvider routeProvider,
        IVehicleCatalog vehicleCatalog,
        IFareCalculator fareCalculator,
        IFareQuoteStore quoteStore,
        Microsoft.Extensions.Options.IOptions<PricingOptions> pricing)
    {
        _routeProvider = routeProvider;
        _vehicleCatalog = vehicleCatalog;
        _fareCalculator = fareCalculator;
        _quoteStore = quoteStore;
        _pricing = pricing.Value;
    }

    public async Task<FareQuoteResponse> CreateAsync(FareQuoteRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var vehicle = _vehicleCatalog.GetByCode(request.VehicleCategory)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["vehicleCategory"] = new[] { "Unknown vehicle category." } });

        // Route info from provider if available; otherwise development estimate.
        RouteInfo? route = null;
        try
        {
            route = await _routeProvider.GetRouteAsync(request.Pickup, request.Drop, cancellationToken);
        }
        catch
        {
            route = null;
        }

        var isDevEstimate = route is null;
        double? distance = request.DistanceKm ?? route?.DistanceKm;
        int? duration = request.DurationMinutes ?? route?.DurationMinutes;

        // If client supplied distance, treat as trusted? Still mark dev estimate if no route provider.
        // If distance supplied by client and route unavailable, use supplied but mark development estimate.
        if (request.DistanceKm.HasValue && route is null)
        {
            isDevEstimate = true;
        }

        var calcRequest = new FareCalculationRequest(
            request.Pickup,
            request.Drop,
            request.TripType,
            vehicle.Code,
            request.TravelDateTime,
            request.ReturnDateTime,
            request.PassengerCount,
            distance,
            duration,
            isDevEstimate);

        var result = _fareCalculator.Calculate(calcRequest);

        var quote = new FareQuote(
            Guid.NewGuid(),
            request.Pickup,
            request.Drop,
            request.TripType,
            vehicle.Code,
            request.TravelDateTime,
            request.ReturnDateTime,
            request.PassengerCount,
            result.Breakdown,
            _pricing.Currency,
            result.IsDevelopmentEstimate,
            DateTimeOffset.UtcNow.AddMinutes(_pricing.QuoteExpirationMinutes),
            distance,
            duration,
            result.Disclaimer);

        await _quoteStore.SaveAsync(quote, cancellationToken);

        return Map(quote);
    }

    public async Task<FareQuoteResponse?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        var quote = await _quoteStore.GetAsync(quoteId, cancellationToken);
        if (quote is null) return null;
        return Map(quote);
    }

    private static void Validate(FareQuoteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Pickup)) errors["pickup"] = new[] { "Pickup is required." };
        if (string.IsNullOrWhiteSpace(request.Drop)) errors["drop"] = new[] { "Drop is required." };
        if (!string.IsNullOrWhiteSpace(request.Pickup) && !string.IsNullOrWhiteSpace(request.Drop) &&
            request.Pickup.Trim().Equals(request.Drop.Trim(), StringComparison.OrdinalIgnoreCase))
            errors["drop"] = new[] { "Pickup and drop-off must be different." };
        if (request.PassengerCount <= 0) errors["passengerCount"] = new[] { "Passenger count must be greater than zero." };
        if (request.VehicleCategory is null) errors["vehicleCategory"] = new[] { "Vehicle category is required." };
        if (request.TripType == TripType.RoundTrip && request.ReturnDateTime is null)
            errors["returnDateTime"] = new[] { "Return date/time is required for round trips." };
        if (request.ReturnDateTime is not null && request.ReturnDateTime <= request.TravelDateTime)
            errors["returnDateTime"] = new[] { "Return date/time must be after travel date/time." };
        if (request.DistanceKm.HasValue && request.DistanceKm < 0) errors["distanceKm"] = new[] { "Distance cannot be negative." };
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private FareQuoteResponse Map(FareQuote q) => new(
        q.Id,
        q.Pickup,
        q.Drop,
        q.TripType,
        q.VehicleCategoryCode,
        q.TravelDateTime,
        q.ReturnDateTime,
        q.PassengerCount,
        new FareBreakdownDto(
            q.Breakdown.BaseFare,
            q.Breakdown.DistanceCharge,
            q.Breakdown.MinimumDistanceCharge,
            q.Breakdown.DriverAllowance,
            q.Breakdown.WaitingCharges,
            q.Breakdown.Tolls,
            q.Breakdown.Parking,
            q.Breakdown.Tax,
            q.Breakdown.Discount,
            q.Breakdown.OtherCharges,
            q.Breakdown.Total),
        q.Currency,
        q.IsDevelopmentEstimate,
        q.Disclaimer,
        q.CreatedAt,
        q.ExpiresAt);
}