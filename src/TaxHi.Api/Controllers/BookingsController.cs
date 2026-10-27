using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Features.Bookings;
using TaxHi.Application.Features.Bookings.Dtos;

namespace TaxHi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly BookingService _service;

    public BookingsController(BookingService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { bookingId = result.BookingId }, result);
    }

    [HttpGet]
    public async Task<ActionResult<BookingListResponse>> List(CancellationToken cancellationToken)
    {
        var items = await _service.ListAsync(cancellationToken);
        return Ok(new BookingListResponse(items, items.Count));
    }

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult<BookingResponse>> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(bookingId, cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{bookingId:guid}/cancel")]
    public async Task<ActionResult<BookingResponse>> Cancel(Guid bookingId, [FromBody] CancelBookingRequest? request, CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(bookingId, request?.Reason, cancellationToken);
        return Ok(result);
    }
}