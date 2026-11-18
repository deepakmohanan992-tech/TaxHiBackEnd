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

    Task<PagedResult<UpcomingBookingDto>> GetUpcomingBookingsAsync(
        string phoneNumber,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default);
}
