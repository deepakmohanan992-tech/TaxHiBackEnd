using TaxHi.Application.Interfaces;

namespace TaxHi.Infrastructure.Services;

public sealed class DevelopmentRouteProvider : IRouteProvider
{
    public Task<RouteInfo?> GetRouteAsync(string pickup, string drop, CancellationToken cancellationToken = default)
    {
        // No live map provider configured. Return null so fare is clearly labelled as development estimate.
        return Task.FromResult<RouteInfo?>(null);
    }
}