using System.Text.Json.Serialization;

namespace TaxHi.Application.Models;

public sealed class AvailableCabListing
{
    public int? VendorId { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleName { get; init; }
    public int? VehicleDetailsId { get; set; }
    public int? VehicleCategoryId { get; set; }
    public int? VehicleBrandId { get; set; }
    public string? FuelType { get; init; }
    public string? VehicleImageURL { get; set; }
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

    // Trip-mode fields (Oneway / RoundTrip / Airport). Null, and left out of the JSON, for the legacy km-based listing.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TripMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? RateAlgorithm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? YardId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? YardName { get; set; }
    /// <summary>Yard -> pickup road distance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? YardToPickupKm { get; set; }
    /// <summary>Drop -> yard road distance (Oneway and airport drop).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? YardToDropKm { get; set; }
    /// <summary>OK, or YardDistanceMissing when no usable yard distance exists (the vehicle is then Unavailable).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? YardDistanceStatus { get; set; }
    /// <summary>Customer journey, pickup -> drop.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? CustomerJourneyKm { get; set; }
    /// <summary>Total billed distance including yard movement.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? TotalCalculatedKm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BilledDays { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? ExtraKm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? ExtraKmAmount { get; set; }
    /// <summary>Airport mode: sum of the applicable airport charges (0 when none). Not part of OriginalAmount.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? AirportCharges { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<AirportChargeItem>? AirportChargeItems { get; set; }
    /// <summary>OriginalAmount + GSTAmount + AirportCharges.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? FinalFare { get; set; }
}
