namespace TaxHi.Application.Models;

/// <summary>
/// Item of the airportTerminals response. Keeps the SelectedItems fields (Id, Value) so existing consumers
/// keep working; the location fields are additive.
/// </summary>
public sealed class AirportTerminalItem
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    /// <summary>Google place id of the terminal (AirportTerminalMaster.GooglePlaceId).</summary>
    public string? PlaceId { get; set; }
}
