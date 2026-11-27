using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Repositories;

public interface IBookingRepository
{
    Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,int userId,
        BookingAmounts amounts,
        string bookingNo,
        string tripCode,
        CancellationToken cancellationToken = default);

    /// <summary>Vendor config row (null when the vendor has none) and its active inclusion/exclusion items.</summary>
    Task<(VendorConfig? Config, IReadOnlyList<VendorItem> Items)> GetVendorConfigurationAsync(
        int? vendorId, int? vehicleBrandId, CancellationToken cancellationToken = default);

    Task<BookingListResponseDto> GetBookingsAsync(
        int userId,
        string mode,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Latest payment attempt of a booking (by BookingId) or of a Razorpay order; null if none.</summary>
    Task<BookingPaymentContext?> GetPaymentContextAsync(
        long? bookingId, string? gatewayOrderId, CancellationToken cancellationToken = default);

    /// <summary>Attaches a Razorpay order to the payment row once; returns the order id now stored (first writer wins).</summary>
    Task<string> SetGatewayOrderAsync(
        long bookingPaymentId, string gatewayOrderId, CancellationToken cancellationToken = default);

    /// <summary>Atomically and idempotently marks the payment successful and confirms the booking.</summary>
    Task<GatewayPaymentResult> ConfirmGatewayPaymentAsync(
        string gatewayOrderId, string gatewayPaymentId, long? amountPaise, string? paymentMethod,
        string? eventId, string? eventType, CancellationToken cancellationToken = default);

    /// <summary>Booking, owner, pickup time and successful payment; null when missing or not owned by <paramref name="userId"/>.</summary>
    Task<BookingCancellationContext?> GetCancellationContextAsync(long bookingId, long userId, CancellationToken cancellationToken = default);

    /// <summary>Runs sp_CancelBooking: validates under a lock and cancels once, storing the manual-refund decision.</summary>
    Task<BookingCancellationResult> CancelBookingAsync(
        long bookingId, long userId, long? tripId, string reason, string? remarks,
        decimal refundAmount, CancellationToken cancellationToken = default);

    /// <summary>Records a failed attempt. Never downgrades a successful payment.</summary>
    Task<GatewayPaymentResult> FailGatewayPaymentAsync(
        string gatewayOrderId, string? gatewayPaymentId, string? reason,
        string? eventId, string? eventType, CancellationToken cancellationToken = default);
}
