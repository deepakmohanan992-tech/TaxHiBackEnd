using System.Collections.Concurrent;
using TaxHi.Application.Interfaces;
using TaxHi.Domain.Entities;

namespace TaxHi.Infrastructure.Repositories;

public sealed class InMemoryFareQuoteStore : IFareQuoteStore
{
    private readonly ConcurrentDictionary<Guid, FareQuote> _store = new();

    public Task SaveAsync(FareQuote quote, CancellationToken cancellationToken = default)
    {
        _store[quote.Id] = quote;
        return Task.CompletedTask;
    }

    public Task<FareQuote?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(quoteId, out var q);
        return Task.FromResult(q);
    }
}

public sealed class InMemoryBookingStore : IBookingStore
{
    private readonly ConcurrentDictionary<Guid, Booking> _store = new();

    public Task SaveAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        _store[booking.Id] = booking;
        return Task.CompletedTask;
    }

    public Task<Booking?> GetAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(bookingId, out var b);
        return Task.FromResult(b);
    }

    public Task<IReadOnlyList<Booking>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Booking>>(_store.Values.OrderByDescending(b => b.CreatedAt).ToList().AsReadOnly());
    }
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<Guid, Payment> _store = new();

    public Task SaveAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        _store[payment.Id] = payment;
        return Task.CompletedTask;
    }

    public Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(paymentId, out var p);
        return Task.FromResult(p);
    }

    public Task<IReadOnlyList<Payment>> GetByBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Payment>>(_store.Values.Where(p => p.BookingId == bookingId).ToList().AsReadOnly());
    }
}

public sealed class InMemoryDriverStore : IDriverStore
{
    private readonly IReadOnlyList<TaxHi.Domain.Entities.Driver> _drivers;

    public InMemoryDriverStore()
    {
        _drivers = TaxHi.Infrastructure.DevelopmentData.SampleDrivers.All;
    }

    public Task<IReadOnlyList<TaxHi.Domain.Entities.Driver>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_drivers);

    public Task<TaxHi.Domain.Entities.Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_drivers.FirstOrDefault(d => d.Id == driverId));
}