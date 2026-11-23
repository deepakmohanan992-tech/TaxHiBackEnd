using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Repositories;

public interface IBookingEmailRepository
{
    /// <summary>Email payload for a booking owned by <paramref name="userId"/>; null when missing or not owned.</summary>
    Task<BookingConfirmationEmailDto?> GetConfirmationEmailDetailsAsync(long bookingId, long userId, CancellationToken cancellationToken = default);

    /// <summary>Writes a row through dbo.SaveTransportVendorAuditLog.</summary>
    Task SaveAuditLogAsync(int vendorId, long userId, string actionName, string details, CancellationToken cancellationToken = default);
}
