namespace TaxHi.Application.Services;

public sealed class BookingOptions
{
    /// <summary>How long an unpaid Pending booking reserves the vehicle.</summary>
    public int HoldDurationMinutes { get; set; } = 15;

    /// <summary>How long to wait for the per-vehicle application lock before returning 409.</summary>
    public int LockTimeoutMilliseconds { get; set; } = 10000;
}
