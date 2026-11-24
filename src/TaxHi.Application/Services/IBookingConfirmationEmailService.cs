namespace TaxHi.Application.Services;

public interface IBookingConfirmationEmailService
{
    /// <summary>
    /// Sends the booking confirmation (with the invoice PDF) to the booking owner, CC the vendor.
    /// Safe to call from any context: needs no HTTP session, and never throws — every failure is
    /// logged to the vendor audit log instead, so a committed booking is never affected.
    /// </summary>
    Task SendBookingConfirmationAsync(long bookingId, long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the cancellation confirmation to the booking owner, CC the vendor staff and the accounts team
    /// (who pay the refund manually). Same guarantees as the confirmation: no HTTP session needed, never throws.
    /// </summary>
    Task SendBookingCancellationAsync(
        long bookingId, long userId, BookingCancellationEmailInfo info, CancellationToken cancellationToken = default);
}

/// <summary>What the cancellation email says besides the booking itself.</summary>
public sealed record BookingCancellationEmailInfo(
    string Reason, string? Remarks, DateTime CancelledDate, decimal RefundAmount, string RefundStatus,
    decimal AmountPaid = 0m, string? PaymentReference = null);
