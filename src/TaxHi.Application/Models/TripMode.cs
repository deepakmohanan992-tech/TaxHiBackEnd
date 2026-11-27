namespace TaxHi.Application.Models;

/// <summary>Trip modes accepted by availableCabListing. Local is reserved: it is accepted but keeps the legacy pricing.</summary>
public static class TripModes
{
    public const string Oneway = "Oneway";
    public const string RoundTrip = "RoundTrip";
    public const string Airport = "Airport";
    public const string Local = "Local";

    public const string AirportPickup = "Pickup";
    public const string AirportDrop = "Drop";

    /// <summary>Canonical spelling of <paramref name="value"/>, or null when it is not a trip mode.</summary>
    public static string? Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ONEWAY" => Oneway,
        "ROUNDTRIP" => RoundTrip,
        "AIRPORT" => Airport,
        "LOCAL" => Local,
        _ => null
    };

    /// <summary>Modes whose fare is built from yard distances (Local is not one of them yet).</summary>
    public static bool UsesYardDistances(string? normalizedMode) => normalizedMode is Oneway or RoundTrip or Airport;

    public static string? NormalizeAirportDirection(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PICKUP" => AirportPickup,
        "DROP" => AirportDrop,
        _ => null
    };

    /// <summary>Oneway and an airport drop end at the drop location, so the drop -> yard distance is needed too.</summary>
    public static bool NeedsDropToYard(string? mode, string? airportDirection) =>
        mode == Oneway || (mode == Airport && airportDirection == AirportDrop);
}
