using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// Tidies unpaid bookings. A lapsed hold already stops blocking the vehicle by time alone (every availability check compares
/// HoldExpiresAtUtc with the clock); this sweeper additionally moves bookings that stayed unpaid past the grace period
/// to Failed and their payment to Failed ("Payment window expired"), so statuses reflect reality.
/// </summary>
public sealed class BookingHoldExpiryService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BookingHoldExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = BookingSettings.ExpirySweepIntervalSeconds(configuration);
        if (interval <= 0)
        {
            logger.LogInformation("Booking hold expiry sweeper is disabled (Booking:ExpirySweepIntervalSeconds = 0).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        try
        {
            do { await SweepAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var grace = BookingSettings.ExpiredHoldGraceMinutes(configuration);

            // A full batch means there may be more; keep going (bounded) instead of waiting a whole interval.
            for (var round = 0; round < 10; round++)
            {
                var expired = await repository.ExpireLapsedHoldsAsync(grace, 200, cancellationToken);
                if (expired > 0) logger.LogInformation("Expired {Count} unpaid booking(s) whose payment window lapsed.", expired);
                if (expired < 200) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking hold expiry sweep failed; it will retry on the next interval.");
        }
    }
}
