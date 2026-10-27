using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Application.Interfaces;

public interface IVehicleCatalog
{
    IReadOnlyList<VehicleCategory> GetAll();
    VehicleCategory? GetByCode(string code);
}

public sealed record VehicleAvailabilityItem(
    string CategoryCode,
    string DisplayName,
    int PassengerCapacity,
    int LuggageCapacity,
    IReadOnlyList<string> Features,
    bool Available,
    string? Reason,
    bool IsSimulated);

public interface IVehicleAvailabilityService
{
    Task<IReadOnlyList<VehicleAvailabilityItem>> GetAvailabilityAsync(
        string pickup,
        string drop,
        DateOnly date,
        TripType tripType,
        CancellationToken cancellationToken = default);
}