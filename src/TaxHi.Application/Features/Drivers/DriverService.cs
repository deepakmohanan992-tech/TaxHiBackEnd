using TaxHi.Application.Interfaces;
using TaxHi.Domain.Entities;

namespace TaxHi.Application.Features.Drivers;

public sealed class DriverService : IDriverService
{
    private readonly IDriverStore _store;

    public DriverService(IDriverStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<Driver>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _store.GetAllAsync(cancellationToken);

    public Task<Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default) =>
        _store.GetAsync(driverId, cancellationToken);

    public async Task<string?> AssignDriverAsync(Guid bookingId, string vehicleCategoryCode, CancellationToken cancellationToken = default)
    {
        var drivers = await _store.GetAllAsync(cancellationToken);
        var candidate = drivers.FirstOrDefault(d => d.IsAvailable && d.CanDrive(vehicleCategoryCode));
        if (candidate is null) return null;
        // Actual assignment status is handled by caller (booking). Return assignment id.
        return $"drv-{candidate.Id:N}";
    }
}