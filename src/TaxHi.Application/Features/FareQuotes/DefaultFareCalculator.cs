using TaxHi.Application.Interfaces;
using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.FareQuotes;

public sealed class DefaultFareCalculator : IFareCalculator
{
    private readonly PricingOptions _pricing;

    public DefaultFareCalculator(Microsoft.Extensions.Options.IOptions<PricingOptions> pricing)
    {
        _pricing = pricing.Value;
    }

    public FareCalculationResult Calculate(FareCalculationRequest request)
    {
        bool isDev = request.IsDevelopmentEstimate;
        string? disclaimer = isDev
            ? "Development estimate only. Route distance/duration not from a live provider."
            : null;

        // Vehicle multiplier based on category (configurable-ish; simple).
        var multiplier = request.VehicleCategoryCode switch
        {
            "hatchback" => 1.0m,
            "sedan" => 1.2m,
            "suv" => 1.5m,
            "innova" => 1.6m,
            _ => 1.0m
        };

        var baseFare = _pricing.BaseFare * multiplier;

        // Distance: use provided or minimum billable distance.
        var distanceKm = request.DistanceKm.HasValue && request.DistanceKm.Value > 0
            ? (decimal)request.DistanceKm.Value
            : _pricing.MinimumBillableDistanceKm;

        var billableDistance = Math.Max(distanceKm, _pricing.MinimumBillableDistanceKm);
        var distanceCharge = billableDistance * _pricing.PerKmRate * multiplier;

        var minimumDistanceCharge = distanceKm < _pricing.MinimumBillableDistanceKm
            ? (_pricing.MinimumBillableDistanceKm - distanceKm) * _pricing.PerKmRate * multiplier
            : 0m;
        // Avoid double counting: if we already bill minimum, set distanceCharge to minimum*rate and min charge 0.
        if (distanceKm < _pricing.MinimumBillableDistanceKm)
        {
            distanceCharge = _pricing.MinimumBillableDistanceKm * _pricing.PerKmRate * multiplier;
            minimumDistanceCharge = 0m;
        }

        // Driver allowance: 1 day for one-way/airport; 2 days for round trip (simplified).
        var days = request.TripType == TripType.RoundTrip ? 2 : 1;
        var driverAllowance = _pricing.DriverAllowancePerDay * days * multiplier;

        // Round trip discount
        var subtotal = baseFare + distanceCharge + driverAllowance;
        var discount = request.TripType == TripType.RoundTrip
            ? subtotal * _pricing.RoundTripDiscountRate
            : 0m;

        var tax = (subtotal - discount) * _pricing.TaxRate;

        var breakdown = new FareBreakdown(
            baseFare,
            distanceCharge,
            minimumDistanceCharge,
            driverAllowance,
            0m,
            0m,
            0m,
            tax,
            discount,
            0m);

        return new FareCalculationResult(breakdown, isDev, disclaimer);
    }
}