using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Repositories;

public interface IBookingInvoiceRepository
{
    /// <summary>Invoice data for a booking owned by <paramref name="userId"/>; null when missing or not owned.</summary>
    Task<BookingInvoiceDto?> GetInvoiceDataAsync(long bookingId, long userId, CancellationToken cancellationToken = default);

    /// <summary>Stored invoice location for a booking owned by <paramref name="userId"/>; null when missing or not owned.</summary>
    Task<BookingInvoiceFileInfo?> GetInvoiceFileInfoAsync(long bookingId, long userId, CancellationToken cancellationToken = default);

    Task SaveInvoicePathAsync(long bookingId, string invoiceFilePath, CancellationToken cancellationToken = default);
}
