namespace TaxHi.Application.Models;

/// <summary>
/// Trip details the API works out itself (the UI sends only the mode, locations and dates):
/// the rate algorithm and, for airport trips, the terminal and whether the customer starts or ends at it.
/// </summary>
public sealed record TripContext(string Mode, int RateAlgorithm, int? AirportTerminalId = null, string? AirportDirection = null);

/// <summary>Outcome of <c>ITripContextService.ResolveAsync</c>: a context, or an error to return as 400.</summary>
public sealed record TripContextResult(TripContext? Context, string? ErrorCode = null, string? ErrorMessage = null)
{
    public bool Succeeded => Context is not null;
}
