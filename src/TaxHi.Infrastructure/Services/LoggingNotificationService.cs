using Microsoft.Extensions.Logging;
using TaxHi.Application.Interfaces;

namespace TaxHi.Infrastructure.Services;

public sealed class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _logger;

    public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyBookingCreatedAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Booking created notification (not actually sent): {BookingId}", bookingId);
        return Task.CompletedTask;
    }

    public Task NotifyBookingCancelledAsync(Guid bookingId, string? reason, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Booking cancelled notification (not actually sent): {BookingId}, reason: {Reason}", bookingId, reason);
        return Task.CompletedTask;
    }

    public Task NotifyDriverAssignedAsync(Guid bookingId, Guid driverId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Driver assigned notification (not actually sent): booking {BookingId}, driver {DriverId}", bookingId, driverId);
        return Task.CompletedTask;
    }
}