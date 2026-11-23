namespace TaxHi.Application.Services;

public interface IBookingConfirmationEmailService
{
    /// <summary>
    /// Sends the booking confirmation (with the invoice PDF) to the booking owner, CC the vendor.
    /// Safe to call from any context: needs no HTTP session, and never throws — every failure is
    /// logged to the vendor audit log instead, so a committed booking is never affected.
    /// </summary>
    Task SendBookingConfirmationAsync(long bookingId, long userId, CancellationToken cancellationToken = default);
}
