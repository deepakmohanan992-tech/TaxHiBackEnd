namespace TaxHi.Application.Models.Booking;

public sealed class SaveBookingPaymentRequest
{
    /// <summary>Client-generated GUID, one per genuine booking submission. Reuse it for double-clicks and retries.</summary>
    public Guid ClientRequestId { get; init; }

    // Customer
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
