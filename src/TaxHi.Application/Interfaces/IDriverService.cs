using TaxHi.Domain.Entities;

namespace TaxHi.Application.Interfaces;

public interface IDriverService
{
    Task<IReadOnlyList<Driver>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default);
    Task<string?> AssignDriverAsync(Guid bookingId, string vehicleCategoryCode, CancellationToken cancellationToken = default);
}