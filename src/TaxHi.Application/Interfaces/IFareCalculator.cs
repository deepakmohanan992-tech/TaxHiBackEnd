using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Application.Interfaces;

public sealed class PricingOptions
{
    public string Currency { get; set; } = "INR";
    public decimal BaseFare { get; set; } = 500m;
    public decimal PerKmRate { get; set; } = 12m;
    public decimal MinimumBillableDistanceKm { get; set; } = 130m;
    public decimal DriverAllowancePerDay { get; set; } = 300m;
    public decimal TaxRate { get; set; } = 0.05m;
    public decimal RoundTripDiscountRate { get; set; } = 0m;
    public int QuoteExpirationMinutes { get; set; } = 30;
}

public interface IFareCalculator
{
    FareCalculationResult Calculate(FareCalculationRequest request);
}

public sealed record FareCalculationRequest(
    string Pickup,
    string Drop,
    TripType TripType,
    string VehicleCategoryCode,
    DateTimeOffset TravelDateTime,
    DateTimeOffset? ReturnDateTime,
    int PassengerCount,
    double? DistanceKm,
    int? DurationMinutes,
    bool IsDevelopmentEstimate);

public sealed record FareCalculationResult(
    FareBreakdown Breakdown,
    bool IsDevelopmentEstimate,
    string? Disclaimer);