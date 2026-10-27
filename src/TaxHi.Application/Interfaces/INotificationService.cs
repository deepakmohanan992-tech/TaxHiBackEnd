namespace TaxHi.Application.Interfaces;

public interface INotificationService
{
    Task NotifyBookingCreatedAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task NotifyBookingCancelledAsync(Guid bookingId, string? reason, CancellationToken cancellationToken = default);
    Task NotifyDriverAssignedAsync(Guid bookingId, Guid driverId, CancellationToken cancellationToken = default);
}