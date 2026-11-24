using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/Booking")]
public sealed class BookingController(IBookingService bookingService, IBookingInvoiceService invoiceService) : ControllerBase
{
    // Guests can check out, matching the guest-booking flow in AuthenticationController.
    [Authorize]
    [HttpPost("SaveBookingPayment")]
    public async Task<ActionResult<CommonResponse<SaveBookingPaymentResponse>>> SaveBookingPayment(
        [FromBody] SaveBookingPaymentRequest request,
        CancellationToken cancellationToken)
    {
        int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var response = await bookingService.SaveBookingPaymentAsync(request, userId, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Streams the booking's invoice PDF. Only the booking's owner can download it; the file is rebuilt if it is missing.
    [Authorize]
    [HttpGet("DownloadInvoice/{bookingId:long}")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> DownloadInvoice(long bookingId, CancellationToken cancellationToken)
    {
        var response = await invoiceService.GetInvoiceFileAsync(bookingId, CurrentUserId(), cancellationToken);
        if (!response.Success || response.Data is null)
            return StatusCode(response.StatusCode, response);

        return PhysicalFile(response.Data.FullPath, "application/pdf", response.Data.FileName);
    }

    // Retry for a booking whose invoice was not generated (or was lost). Idempotent; never creates a booking.
    [Authorize]
    [HttpPost("GenerateInvoice/{bookingId:long}")]
    public async Task<ActionResult<CommonResponse<BookingInvoiceResult>>> GenerateInvoice(long bookingId, CancellationToken cancellationToken)
    {
        var response = await invoiceService.RetryInvoiceAsync(bookingId, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Step 1 of cancelling: sends an OTP to the account's registered mobile number.
    [Authorize]
    [HttpPost("SendCancellationOtp")]
    public async Task<ActionResult<CommonResponse<object>>> SendCancellationOtp(
        [FromBody] SendCancellationOtpRequest request,
        CancellationToken cancellationToken)
    {
        var response = await bookingService.SendCancellationOtpAsync(request, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Step 2: cancels the signed-in user's own booking. A valid OTP is mandatory; the user id comes from the token.
    [Authorize]
    [HttpPost("CancelBooking")]
    public async Task<ActionResult<CommonResponse<CancelBookingResponse>>> CancelBooking(
        [FromBody] CancelBookingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await bookingService.CancelBookingAsync(request, CurrentUserId(), cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    private int CurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    // Returns the signed-in user's own bookings for one tab (Upcoming / Current / History), matched on the user id in their token.
    [Authorize]
    [HttpGet("GetBookings")]
    public async Task<ActionResult<CommonResponse<BookingListResponseDto>>> GetBookings(
        [FromQuery] string mode = "Upcoming",
        [FromQuery] int currentPage = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var response = await bookingService.GetBookingsAsync(CurrentUserId(), mode, currentPage, pageSize, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
