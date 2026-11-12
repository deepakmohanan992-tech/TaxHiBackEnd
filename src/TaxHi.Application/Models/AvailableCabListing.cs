namespace TaxHi.Application.Models;

public sealed class AvailableCabListing
{
    public string? VehicleName { get; init; }
    public string? FuelType { get; init; }
    public string? VehicleImageURL { get; init; }
    public string? VehicleDescription { get; init; }
    public decimal? DiscountPercentage { get; init; }
    public decimal? OriginalAmount { get; init; }
    public decimal? DiscountedAmount { get; init; }
    public decimal? GSTPercentage { get; init; }
    public decimal? GSTAmount { get; init; }
    public decimal? IncludedKilometres { get; init; }
    public decimal? PostLimitChargePerKM { get; init; }
    public string? Inclusions { get; init; }
    public string? AvailabilityStatus { get; init; }
    public string? AvailableFrom { get; set; }
}
