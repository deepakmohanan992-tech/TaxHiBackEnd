using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Services;

public interface IBookingService
{
    Task<CommonResponse<SaveBookingPaymentResponse>> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Bookings of the signed-in user for one tab (Upcoming / Current / History), with pagination and tab counts.</summary>
    Task<CommonResponse<BookingListResponseDto>> GetBookingsAsync(
        int userId,
        string? mode,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default);
}
