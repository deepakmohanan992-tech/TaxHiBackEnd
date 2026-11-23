using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Services;

public interface IBookingInvoiceService
{
    /// <summary>
    /// Generates (or, unless <paramref name="regenerate"/>, reuses) the invoice PDF of a booking the user owns.
    /// Never throws for PDF/storage failures: those are logged and reported as <c>InvoiceGenerated = false</c>,
    /// so a committed booking is never reported as failed.
    /// </summary>
    Task<BookingInvoiceResult> EnsureInvoiceAsync(
        long bookingId, long userId, string? bookingNo = null, bool regenerate = false,
        CancellationToken cancellationToken = default);

    /// <summary>Maps a stored invoice path to its full file path; null when it is empty, outside the storage folder or missing.</summary>
    string? ResolveExistingInvoicePath(string? storedRelativePath);

    /// <summary>Retry endpoint: 404 for unknown/foreign bookings, 500 when generation fails again.</summary>
    Task<CommonResponse<BookingInvoiceResult>> RetryInvoiceAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default);

    /// <summary>Resolves the PDF for download, regenerating it first if the file is missing.</summary>
    Task<CommonResponse<BookingInvoiceFile>> GetInvoiceFileAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default);
}
