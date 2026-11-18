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
        return new ObjectResult(response) { StatusCode = response.StatusCode };
    }

    // Streams the booking's invoice PDF. Only the booking's owner can download it; the file is rebuilt if it is missing.
    [Authorize]
    [HttpGet("DownloadInvoice/{bookingId:long}")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> DownloadInvoice(long bookingId, CancellationToken cancellationToken)
    {
        var response = await invoiceService.GetInvoiceFileAsync(bookingId, CurrentUserId(), cancellationToken);
        if (!response.Success || response.Data is null)
            return new ObjectResult(response) { StatusCode = response.StatusCode };

        return PhysicalFile(response.Data.FullPath, "application/pdf", response.Data.FileName);
    }

    // Retry for a booking whose invoice was not generated (or was lost). Idempotent; never creates a booking.
    [Authorize]
    [HttpPost("GenerateInvoice/{bookingId:long}")]
    public async Task<ActionResult<CommonResponse<BookingInvoiceResult>>> GenerateInvoice(long bookingId, CancellationToken cancellationToken)
    {
        var response = await invoiceService.RetryInvoiceAsync(bookingId, CurrentUserId(), cancellationToken);
        return new ObjectResult(response) { StatusCode = response.StatusCode };
    }

    private int CurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    // Returns the signed-in user's own upcoming bookings, matched on the mobile number in their token.
    [Authorize]
    [HttpGet("UpcomingBookings")]
    public async Task<ActionResult<CommonResponse<PagedResult<UpcomingBookingDto>>>> GetUpcomingBookings(
        [FromQuery] int currentPage = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var mobileNumber = User.FindFirst(ClaimTypes.MobilePhone)?.Value;
        var response = await bookingService.GetUpcomingBookingsAsync(mobileNumber, currentPage, pageSize, cancellationToken);
        return new ObjectResult(response) { StatusCode = response.StatusCode };
    }
}
