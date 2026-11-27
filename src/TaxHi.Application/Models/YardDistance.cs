namespace TaxHi.Application.Models;

/// <summary>An active vendor yard (TransportVendorYard).</summary>
public sealed class VendorYard
{
    public int YardId { get; set; }
    public int VendorId { get; set; }
    public string? YardName { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

/// <summary>
/// Road distances of one yard to the customer's pickup and drop. A null distance means "could not be resolved":
/// the procedure never treats it as 0 km.
/// </summary>
public sealed record YardDistance(int YardId, decimal? ToPickupKm, decimal? ToDropKm);

/// <summary>One airport charge applied to a listed vehicle.</summary>
public sealed class AirportChargeItem
{
    public string ChargeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
