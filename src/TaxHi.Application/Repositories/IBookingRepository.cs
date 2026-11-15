using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Repositories;

public interface IBookingRepository
{
    Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,
        BookingAmounts amounts,
        string bookingNo,
        CancellationToken cancellationToken = default);
}
