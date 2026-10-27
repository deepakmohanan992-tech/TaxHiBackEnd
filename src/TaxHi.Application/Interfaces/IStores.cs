using TaxHi.Domain.Entities;

namespace TaxHi.Application.Interfaces;

public interface IFareQuoteStore
{
    Task SaveAsync(FareQuote quote, CancellationToken cancellationToken = default);
    Task<FareQuote?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default);
}

public interface IBookingStore
{
    Task SaveAsync(Booking booking, CancellationToken cancellationToken = default);
    Task<Booking?> GetAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Booking>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentStore
{
    Task SaveAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payment>> GetByBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);
}