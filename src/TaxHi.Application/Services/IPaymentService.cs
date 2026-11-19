using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Application.Services;

public interface IPaymentService
{
    /// <summary>
    /// Saves the pending booking through the existing booking flow (idempotent on ClientRequestId) and creates
    /// the Razorpay order for the server-calculated payable amount.
    /// </summary>
    Task<CommonResponse<RazorpayOrderResponse>> CreateOrderAsync(
        SaveBookingPaymentRequest request, int userId, CancellationToken cancellationToken = default);

    /// <summary>Verifies the checkout signature server-side and, only then, confirms the payment and booking.</summary>
    Task<CommonResponse<PaymentVerificationResponse>> VerifyPaymentAsync(
        VerifyRazorpayPaymentRequest request, int userId, CancellationToken cancellationToken = default);

    /// <summary>Processes a Razorpay webhook (raw body + signature header) idempotently.</summary>
    Task<CommonResponse<string>> HandleWebhookAsync(
        string rawBody, string? signature, string? eventId, CancellationToken cancellationToken = default);
}
