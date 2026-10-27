using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Payments;
using TaxHi.Application.Features.Payments.Dtos;
using TaxHi.Domain.Entities;

namespace TaxHi.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentService _service;
    private readonly IPaymentStore _store;

    public PaymentsController(PaymentService service, IPaymentStore store)
    {
        _service = service;
        _store = store;
    }

    [HttpPost("create-order")]
    public async Task<ActionResult<PaymentResponse>> CreateOrder([FromBody] CreatePaymentOrderRequest request, CancellationToken cancellationToken)
    {
        var payment = await _service.CreateOrderAsync(request.BookingId, request.Amount ?? 0m, "INR", cancellationToken);
        return Ok(Map(payment));
    }

    [HttpGet("{paymentId:guid}/status")]
    public async Task<ActionResult<PaymentResponse>> Status(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _store.GetAsync(paymentId, cancellationToken);
        if (payment is null) return NotFound();
        return Ok(Map(payment));
    }

    private static PaymentResponse Map(Payment p) => new(
        p.Id,
        p.BookingId,
        p.Amount,
        p.Currency,
        p.Status.ToString(),
        p.ProviderOrderId,
        p.CreatedAt,
        p.UpdatedAt);
}