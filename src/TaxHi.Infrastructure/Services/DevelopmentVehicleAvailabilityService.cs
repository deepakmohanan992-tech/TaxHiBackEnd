using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Vehicles.Dtos;
using TaxHi.Domain.Enums;
using TaxHi.Infrastructure.DevelopmentData;

namespace TaxHi.Infrastructure.Services;

public sealed class DevelopmentVehicleAvailabilityService : IVehicleAvailabilityService
{
    public Task<IReadOnlyList<VehicleAvailabilityItem>> GetAvailabilityAsync(
        string pickup,
        string drop,
        DateOnly date,
        TripType tripType,
        CancellationToken cancellationToken = default)
    {
        var isPast = date < DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var items = SampleVehicles.All.Select(v => new VehicleAvailabilityItem(
            v.Code,
            v.DisplayName,
            v.PassengerCapacity,
            v.LuggageCapacity,
            v.Features,
            !isPast,
            isPast ? "Travel date is in the past." : null,
            true)).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<VehicleAvailabilityItem>>(items);
    }
}