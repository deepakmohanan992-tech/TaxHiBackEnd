namespace TaxHi.Application.Models.Booking;

/// <summary>Everything the booking confirmation email needs, from USP_GetBookingConfirmationEmailDetails.</summary>
public sealed class BookingConfirmationEmailDto
{
    public long BookingId { get; set; }
    public string BookingNo { get; set; } = string.Empty;
    public long UserId { get; set; }
    public int VendorId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public string? TripType { get; set; }
    public DateTime? PickupDateTime { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropLocation { get; set; }
    public string? PickupCity { get; set; }
    public string? DropCity { get; set; }
    public decimal? DistanceKm { get; set; }
    public decimal? IncludedKilometres { get; set; }
    public decimal? ExtraKmRate { get; set; }

    public string? CarType { get; set; }
    public string? InvoiceFilePath { get; set; }

    // Vendor SMTP identity and CC addresses (never exposed outside the server).
    public string? VendorName { get; set; }
    public string? VendorWebsite { get; set; }
    public string? GmailUserName { get; set; }
    public string? GmailAppPassword { get; set; }
    public string? ReservationStaffEmail { get; set; }
    public string? ContactPersonEmail { get; set; }
}
