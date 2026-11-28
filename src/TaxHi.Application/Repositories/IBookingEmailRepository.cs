using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Repositories;

public interface IBookingEmailRepository
{
    /// <summary>Email payload for a booking owned by <paramref name="userId"/>; null when missing or not owned.</summary>
    Task<BookingConfirmationEmailDto?> GetConfirmationEmailDetailsAsync(long bookingId, long userId, CancellationToken cancellationToken = default);
}
