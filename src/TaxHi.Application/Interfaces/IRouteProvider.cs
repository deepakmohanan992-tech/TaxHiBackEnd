namespace TaxHi.Application.Interfaces;

public sealed record RouteInfo(double DistanceKm, int DurationMinutes, string Source);

public interface IRouteProvider
{
    Task<RouteInfo?> GetRouteAsync(string pickup, string drop, CancellationToken cancellationToken = default);
}