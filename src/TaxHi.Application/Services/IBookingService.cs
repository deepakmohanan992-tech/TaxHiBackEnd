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

    /// <summary>Sends the cancellation OTP to the signed-in user's registered mobile number for one of their cancellable bookings.</summary>
    Task<CommonResponse<object>> SendCancellationOtpAsync(
        SendCancellationOtpRequest request,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels the signed-in user's own booking, requires a valid OTP, records the refund due (paid manually by accounts) and notifies the customer and accounts team.</summary>
    Task<CommonResponse<CancelBookingResponse>> CancelBookingAsync(
        CancelBookingRequest request,
        int userId,
        CancellationToken cancellationToken = default);
}
