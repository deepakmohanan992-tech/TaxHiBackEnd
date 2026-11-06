namespace TaxHi.Application.Models;

public sealed class AvailableCabListing
{
    public string? VehicleName { get; init; }
    public string? AvailabilityStatus { get; init; }
    public string? SimilarVehicleNames { get; init; }
    public string? VehicleImageURL { get; init; }
    public int? SeatingCapacity { get; init; }
    public bool? IsAirCondition { get; init; }
    public decimal? IncludedKilometres { get; init; }
    public decimal? PostLimitChargePerKM { get; init; }
    public string? Inclusions { get; init; }
    public decimal? GSTPercentage { get; init; }
    public decimal? MinimumKMRate { get; init; }
}
