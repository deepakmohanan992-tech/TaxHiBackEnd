namespace TaxHi.Application.Models;

public sealed class AvailableCabListingRequestDto : PaginationRequestDto
{
    public int? VendorId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int? LocationFrom { get; init; }
    public int? LocationTo { get; init; }
}
