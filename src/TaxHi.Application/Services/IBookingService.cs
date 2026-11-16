using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Services;

public interface IBookingService
{
    Task<CommonResponse<SaveBookingPaymentResponse>> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Upcoming bookings of the customer with the given phone number, soonest pickup first.</summary>
    Task<CommonResponse<PagedResult<UpcomingBookingDto>>> GetUpcomingBookingsAsync(
        string? phoneNumber,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default);
}
