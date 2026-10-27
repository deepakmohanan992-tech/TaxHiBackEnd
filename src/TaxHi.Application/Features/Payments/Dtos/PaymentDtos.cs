namespace TaxHi.Application.Features.Payments.Dtos;

public sealed record CreatePaymentOrderRequest(Guid BookingId, decimal? Amount = null);

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid BookingId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderOrderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);