using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.FareQuotes.Dtos;

public sealed record FareQuoteRequest(
    string Pickup,
    string Drop,
    TripType TripType,
    DateTimeOffset TravelDateTime,
    DateTimeOffset? ReturnDateTime,
    string VehicleCategory,
    int PassengerCount,
    double? DistanceKm,
    int? DurationMinutes);

public sealed record FareBreakdownDto(
    decimal BaseFare,
    decimal DistanceCharge,
    decimal MinimumDistanceCharge,
    decimal DriverAllowance,
    decimal WaitingCharges,
    decimal Tolls,
    decimal Parking,
    decimal Tax,
    decimal Discount,
    decimal OtherCharges,
    decimal Total);

public sealed record FareQuoteResponse(
    Guid QuoteId,
    string Pickup,
    string Drop,
    TripType TripType,
    string VehicleCategory,
    DateTimeOffset TravelDateTime,
    DateTimeOffset? ReturnDateTime,
    int PassengerCount,
    FareBreakdownDto Breakdown,
    string Currency,
    bool IsDevelopmentEstimate,
    string? Disclaimer,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);