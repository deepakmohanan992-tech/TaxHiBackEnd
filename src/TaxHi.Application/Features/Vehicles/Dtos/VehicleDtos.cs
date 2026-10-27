using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.Vehicles.Dtos;

public sealed record VehicleCategoryDto(
    string Code,
    string DisplayName,
    int PassengerCapacity,
    int LuggageCapacity,
    IReadOnlyList<string> Features);

public sealed record VehicleAvailabilityItemDto(
    string CategoryCode,
    string DisplayName,
    int PassengerCapacity,
    int LuggageCapacity,
    IReadOnlyList<string> Features,
    bool Available,
    string? Reason,
    bool IsSimulated);

public sealed record VehicleAvailabilityResponse(
    string Pickup,
    string Drop,
    DateOnly Date,
    TripType TripType,
    IReadOnlyList<VehicleAvailabilityItemDto> Items,
    bool IsSimulated,
    string? Disclaimer);