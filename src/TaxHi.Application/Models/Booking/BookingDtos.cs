namespace TaxHi.Application.Models.Booking;

public sealed class SaveBookingPaymentRequest
{
    /// <summary>Client-generated GUID, one per genuine booking submission. Reuse it for double-clicks and retries.</summary>
    public Guid ClientRequestId { get; init; }

    // Customer
    public int UserId { get; init; } // internal because the API controller extracts it from the token
    public string Name { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? AlternateEmail { get; init; }
    public bool HasGST { get; init; }
    public string? GSTNumber { get; init; }
    public string? GSTCompanyName { get; init; }

    // Trip
    public string TripType { get; init; } = string.Empty;
    public int FromCityId { get; init; }
    public int ToCityId { get; init; }
    public string? Route { get; init; }
    public DateTime PickupDateTime { get; init; }
    public DateOnly? DropDate { get; init; }
    public string PickupLocation { get; init; } = string.Empty;
    public string DropLocation { get; init; } = string.Empty;

    // Vehicle
    public int VendorId { get; init; }
    public int VehicleId { get; init; }
    public int VehicleDetailsId { get; init; }
    public int VehicleCategoryId { get; init; }
    public int VehicleBrandId { get; init; }
    public string VehicleName { get; init; } = string.Empty;
    public string? FuelType { get; init; }

    // Payment / fare
    public string PaymentOption { get; init; } = string.Empty; // PartPay | FullPay
    public string? CouponCode { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal BaseFare { get; init; }
    public decimal DriverAllowance { get; init; }
    public decimal AddOnsTotalAmount { get; init; }
    public decimal GSTAmount { get; init; }
    public decimal TotalFare { get; init; }
    public decimal PayableNowAmount { get; init; }
    public decimal BalancePayableAmount { get; init; }

    public List<BookingAddOnDto>? AddOns { get; init; }
}

public sealed class BookingAddOnDto
{
    public int AddOnId { get; init; }
    public string AddOnName { get; init; } = string.Empty;
    public string PriceType { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public decimal CalculatedAmount { get; init; }
}

public sealed class SaveBookingPaymentResponse
{
    public long BookingId { get; set; }
    public string? BookingNo { get; set; }
    public long BookingPaymentId { get; set; }
    public string BookingStatus { get; set; } = "Pending";
    public string PaymentStatus { get; set; } = "Pending";
    public DateTime? HoldExpiresAtUtc { get; set; }
    /// <summary>True when this ClientRequestId was already processed and the original booking is returned.</summary>
    public bool IsDuplicateRequest { get; set; }
    public decimal TotalFare { get; set; }
    public decimal PayableNowAmount { get; set; }
    public decimal BalancePayableAmount { get; set; }
}

/// <summary>Server-computed, rounded amounts passed to the repository.</summary>
public sealed record BookingAmounts(decimal TotalFare, decimal PayableNowAmount, decimal BalancePayableAmount);

/// <summary>One upcoming booking with its payment summary, as returned by USP_GetUpcomingBookings.</summary>
public sealed class UpcomingBookingDto
{
    public long BookingId { get; set; }
    public string? BookingNo { get; set; }
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? AlternateEmail { get; set; }
    public bool HasGST { get; set; }
    public string? GSTNumber { get; set; }
    public string? GSTCompanyName { get; set; }

    // Trip
    public DateTime PickupDateTime { get; set; }
    public DateTime? DropDate { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropLocation { get; set; }
    public int? FromCityId { get; set; }
    public int? ToCityId { get; set; }
    public string? Route { get; set; }
    public string? TripType { get; set; }

    // Vehicle
    public int? VendorId { get; set; }
    public int? VehicleId { get; set; }
    public int? VehicleDetailsId { get; set; }
    public int? VehicleCategoryId { get; set; }
    public int? VehicleBrandId { get; set; }
    public string? VehicleName { get; set; }
    public string? FuelType { get; set; }

    // Fare
    public string? PaymentOption { get; set; }
    public string? CouponCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal BaseFare { get; set; }
    public decimal DriverAllowance { get; set; }
    public decimal AddOnsTotalAmount { get; set; }
    public decimal GSTAmount { get; set; }
    /// <summary>Booking amount.</summary>
    public decimal TotalFare { get; set; }
    public decimal PayableNowAmount { get; set; }
    public decimal BalancePayableAmount { get; set; }

    // Booking state: Pending | Confirmed (Cancelled, Failed and expired holds are never returned)
    public string BookingStatus { get; set; } = string.Empty;
    public DateTime? HoldExpiresAtUtc { get; set; }
    public DateTime CreatedDateUtc { get; set; }
    public DateTime ModifiedDateUtc { get; set; }

    // Payment (latest payment attempt; null/defaults when the booking has no payment row)
    public long? BookingPaymentId { get; set; }
    public string? PaymentGateway { get; set; }
    public string? PaymentGatewayOrderId { get; set; }
    public string? PaymentGatewayPaymentId { get; set; }
    public string? TransactionReference { get; set; }
    /// <summary>Status of the latest payment attempt: Pending | Success | Failed | Refunded.</summary>
    public string PaymentStatus { get; set; } = "Pending";
    /// <summary>Amount of the latest payment attempt, whatever its status.</summary>
    public decimal PaymentAmount { get; set; }
    public string? PaymentMethod { get; set; }
    /// <summary>Advance paid: the sum of successful payments only.</summary>
    public decimal AdvancePaidAmount { get; set; }
    /// <summary>TotalFare minus AdvancePaidAmount.</summary>
    public decimal BalanceDueAmount { get; set; }
    /// <summary>When the most recent successful payment was made; null if nothing has been paid yet.</summary>
    public DateTime? PaidDateUtc { get; set; }
}
