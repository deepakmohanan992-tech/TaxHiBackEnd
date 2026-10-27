using TaxHi.Domain.Entities;

namespace TaxHi.Application.Interfaces;

public interface ILocationService
{
    Task<IReadOnlyList<Location>> SearchAsync(string query, CancellationToken cancellationToken = default);
}