using TaxHi.Domain.Entities;

namespace TaxHi.Application.Interfaces;

public interface IDriverStore
{
    Task<IReadOnlyList<Driver>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default);
}