using Microsoft.Extensions.Configuration;

namespace TaxHi.Infrastructure.Common;

/// <summary>Typed reads of the "Booking" section of appsettings.json. No second source of values: defaults apply only when a key is absent.</summary>
public static class BookingSettings
{
    /// <summary>How long an unpaid Pending booking reserves the vehicle.</summary>
    public static int HoldDurationMinutes(IConfiguration c) => c.GetValue("Booking:HoldDurationMinutes", 15);

    /// <summary>How long to wait for the per-vehicle application lock before returning 409.</summary>
    public static int LockTimeoutMilliseconds(IConfiguration c) => c.GetValue("Booking:LockTimeoutMilliseconds", 10000);

    /// <summary>Cancelling at least this many hours before pickup refunds the full amount paid.</summary>
    public static int FullRefundHoursBeforePickup(IConfiguration c) => c.GetValue("Booking:FullRefundHoursBeforePickup", 24);

    /// <summary>Cancelling at least this many hours (inside the full-refund window) before pickup refunds PartialRefundPercent.</summary>
    public static int PartialRefundHoursBeforePickup(IConfiguration c) => c.GetValue("Booking:PartialRefundHoursBeforePickup", 12);

    public static decimal PartialRefundPercent(IConfiguration c) => c.GetValue("Booking:PartialRefundPercent", 50m);
}
