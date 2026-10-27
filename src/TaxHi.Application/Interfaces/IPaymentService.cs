using TaxHi.Domain.Entities;

namespace TaxHi.Application.Interfaces;

public interface IPaymentService
{
    Task<Payment> CreateOrderAsync(Guid bookingId, decimal amount, string currency, CancellationToken cancellationToken = default);
    Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);
}