using TaxHi.Application.Exceptions;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Payments.Dtos;
using TaxHi.Domain.Entities;

namespace TaxHi.Application.Features.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentStore _store;
    private readonly IBookingStore _bookingStore;

    public PaymentService(IPaymentStore store, IBookingStore bookingStore)
    {
        _store = store;
        _bookingStore = bookingStore;
    }

    public async Task<Payment> CreateOrderAsync(Guid bookingId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingStore.GetAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        var payAmount = amount > 0 ? amount : booking.FareTotal;
        var payCurrency = string.IsNullOrWhiteSpace(currency) ? booking.Currency : currency;

        var payment = new Payment(Guid.NewGuid(), bookingId, payAmount, payCurrency);
        // Provider order id is simulated; real provider must set after verification.
        payment.SetProviderOrder($"dev-order-{payment.Id:N}");
        await _store.SaveAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await _store.GetAsync(paymentId, cancellationToken);
    }
}