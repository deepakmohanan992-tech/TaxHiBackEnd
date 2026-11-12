namespace TaxHi.Application.Models;

public sealed class AvailableCabListingRequestDto : PaginationRequest
{
    public int? VendorId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int? LocationFrom { get; init; }
    public int? LocationTo { get; init; }
    public List<int>? VehicleType { get; init; }
    public int Seats { get; init; } = -1;
    public int? Recommended { get; init; }
}
