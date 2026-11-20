using TaxHi.Application.Models.Maps;

namespace TaxHi.Application.Repositories;

public interface IBookingRouteRepository
{
    /// <summary>Inserts or updates the Google route details of a booking (one row per booking).</summary>
    Task SaveRouteDetailsAsync(long bookingId, BookingRouteDetailsDto route, CancellationToken cancellationToken = default);
}
