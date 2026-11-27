using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/Payment/razorpay")]
public sealed class PaymentController(IPaymentService paymentService) : ControllerBase
{
    // Takes the same body as Booking/SaveBookingPayment. Fare, payable amount and Razorpay amount are computed by the backend.
    [Authorize]
    [RequestTimeout("payment")]
    [HttpPost("CreateOrder")]
    public async Task<ActionResult<CommonResponse<RazorpayOrderResponse>>> CreateOrder(
        [FromBody] SaveBookingPaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await paymentService.CreateOrderAsync(request, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [Authorize]
    [RequestTimeout("payment")]
    [HttpPost("VerifyPayment")]
    public async Task<ActionResult<CommonResponse<PaymentVerificationResponse>>> VerifyPayment(
        [FromBody] VerifyRazorpayPaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await paymentService.VerifyPaymentAsync(request, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // The customer closed / cancelled the payment window: marks the attempt failed and releases the vehicle hold
    // (after the backend has checked with Razorpay that the order was not actually paid). The user comes from the token.
    [Authorize]
    [RequestTimeout("payment")]
    [HttpPost("CancelPayment")]
    public async Task<ActionResult<CommonResponse<PaymentCancellationResponse>>> CancelPayment(
        [FromBody] CancelPaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await paymentService.CancelPaymentAsync(request, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Called by Razorpay, so no JWT; authenticity comes from the X-Razorpay-Signature HMAC over the raw body.
    [AllowAnonymous]
    [DisableRequestTimeout]
    [HttpPost("RazorpayWebhook")]
    public async Task<ActionResult<CommonResponse<string>>> RazorpayWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        var response = await paymentService.HandleWebhookAsync(
            rawBody,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    private int CurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
}
