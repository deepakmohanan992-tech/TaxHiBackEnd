using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/Booking")]
public sealed class BookingController(IBookingService bookingService) : ControllerBase
{
    // Guests can check out, matching the guest-booking flow in AuthenticationController.
    [AllowAnonymous]
    [HttpPost("SaveBookingPayment")]
    public async Task<ActionResult<CommonResponse<SaveBookingPaymentResponse>>> SaveBookingPayment(
        [FromBody] SaveBookingPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await bookingService.SaveBookingPaymentAsync(request, cancellationToken);
        return new ObjectResult(response) { StatusCode = response.StatusCode };
    }
}
