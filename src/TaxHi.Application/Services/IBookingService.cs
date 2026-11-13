using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Services;

public interface IBookingService
{
    Task<CommonResponse<SaveBookingPaymentResponse>> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,
        CancellationToken cancellationToken = default);
}
