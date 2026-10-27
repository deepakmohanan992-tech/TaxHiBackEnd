using TaxHi.Application.Interfaces;
using TaxHi.Domain.Entities;
using TaxHi.Infrastructure.DevelopmentData;

namespace TaxHi.Infrastructure.Services;

public sealed class DevelopmentLocationService : ILocationService
{
    private readonly ICacheService _cache;

    public DevelopmentLocationService(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<Location>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var key = $"locations:search:{(query ?? string.Empty).Trim().ToLowerInvariant()}";
        var cached = await _cache.GetAsync<List<Location>>(key, cancellationToken);
        if (cached is not null) return cached.AsReadOnly();

        var q = (query ?? string.Empty).Trim();
        var results = string.IsNullOrWhiteSpace(q)
            ? SampleLocations.All.ToList()
            : SampleLocations.All
                .Where(l => l.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            l.City.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();

        await _cache.SetAsync(key, results, TimeSpan.FromMinutes(10), cancellationToken);
        return results.AsReadOnly();
    }
}