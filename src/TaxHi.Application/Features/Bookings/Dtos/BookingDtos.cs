using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.Bookings.Dtos;

public sealed record CreateBookingRequest(
    Guid? QuoteId,
    string Pickup,
    string Drop,
    TripType TripType,
    DateTimeOffset TravelDateTime,
    DateTimeOffset? ReturnDateTime,
    string VehicleCategory,
    int PassengerCount,
    string ContactName,
    string ContactPhone,
    string? ContactEmail);

public sealed record CancelBookingRequest(string? Reason);

public sealed record BookingResponse(
    Guid BookingId,
    string Pickup,
    string Drop,
    TripType TripType,
    string VehicleCategory,
    DateTimeOffset TravelDateTime,
    DateTimeOffset? ReturnDateTime,
    int PassengerCount,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    decimal FareTotal,
    string Currency,
    string Status,
    Guid? QuoteId,
    string? DriverAssignmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

public sealed record BookingListResponse(IReadOnlyList<BookingResponse> Items, int TotalCount);