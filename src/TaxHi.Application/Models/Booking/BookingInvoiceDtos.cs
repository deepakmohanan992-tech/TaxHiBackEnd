namespace TaxHi.Application.Models.Booking;

/// <summary>Everything the invoice PDF prints, as returned by USP_GetBookingInvoiceData.</summary>
public sealed class BookingInvoiceDto
{
    public long BookingId { get; set; }
    public string BookingNo { get; set; } = string.Empty;
    public long VehicleBookingMasterId { get; set; }
    public long UserId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public DateTime CreatedDateUtc { get; set; }

    // Customer
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? AlternateEmail { get; set; }
    public bool HasGST { get; set; }
    public string? GSTNumber { get; set; }
    public string? GSTCompanyName { get; set; }

    // Journey
    public string? TripType { get; set; }
    public string? Route { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropLocation { get; set; }
    public DateTime? PickupDateTime { get; set; }
    public DateTime? ReturnDateTime { get; set; }
    public DateTime? DropDate { get; set; }
    public string? FromCity { get; set; }
    public string? ToCity { get; set; }

    // Vehicle
    public string? VehicleName { get; set; }
    public string? FuelType { get; set; }
    public string? RegistrationNo { get; set; }
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }

    // Fare
    public string? PaymentOption { get; set; }
    public string? CouponCode { get; set; }
    public decimal BaseFare { get; set; }
    public decimal DriverAllowance { get; set; }
    public decimal AddOnsTotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GSTAmount { get; set; }
    public decimal TotalFare { get; set; }
    public decimal PayableNowAmount { get; set; }

    // Payment (AmountPaid counts successful payments only)
    public decimal AmountPaid { get; set; }
    public DateTime? PaidDateUtc { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentMethod { get; set; }

    public List<BookingInvoiceAddOnDto> AddOns { get; set; } = [];
    public List<BookingInvoicePaymentDto> Payments { get; set; } = [];
}

public sealed class BookingInvoiceAddOnDto
{
    public int AddOnId { get; set; }
    public string AddOnName { get; set; } = string.Empty;
    public string PriceType { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal CalculatedAmount { get; set; }
}

public sealed class BookingInvoicePaymentDto
{
    public long BookingPaymentId { get; set; }
    public string? PaymentOption { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public decimal PaymentAmount { get; set; }
    public string? TransactionReference { get; set; }
    public DateTime? PaidDateUtc { get; set; }
    public DateTime CreatedDateUtc { get; set; }
}

/// <summary>Stored invoice location for a booking, from USP_GetBookingInvoiceFile.</summary>
public sealed class BookingInvoiceFileInfo
{
    public long BookingId { get; set; }
    public string BookingNo { get; set; } = string.Empty;
    public string? InvoiceFilePath { get; set; }
    public DateTime? InvoiceGeneratedAtUtc { get; set; }
}

/// <summary>Outcome of an invoice generation attempt. A failed attempt never affects the booking itself.</summary>
public sealed class BookingInvoiceResult
{
    public long BookingId { get; set; }
    public string? BookingNo { get; set; }
    public bool InvoiceGenerated { get; set; }
    public string? InvoiceUrl { get; set; }
}

/// <summary>A readable invoice PDF, ready to stream to the client.</summary>
public sealed class BookingInvoiceFile
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
}
