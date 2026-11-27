using System.ComponentModel.DataAnnotations;

namespace TaxHi.Application.Models;

public sealed class AvailableCabListingRequestDto : PaginationRequest
{
    public int? VendorId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }

    [StringLength(300)] public string? PickupPlaceId { get; init; }
    [Range(-90, 90)] public decimal? PickupLatitude { get; init; }
    [Range(-180, 180)] public decimal? PickupLongitude { get; init; }
    [StringLength(300)] public string? DropPlaceId { get; init; }
    [Range(-90, 90)] public decimal? DropLatitude { get; init; }
    [Range(-180, 180)] public decimal? DropLongitude { get; init; }

    public List<int>? VehicleType { get; init; }
    public int Seats { get; init; } = -1;
    public int? Recommended { get; init; }

    /// <summary>Oneway | RoundTrip | Airport | Local. Omitted (or Local) keeps the existing km-based pricing. The rate algorithm and airport terminal/direction are worked out by the API.</summary>
    [StringLength(20)] public string? Mode { get; init; }
}
