using Microsoft.Extensions.Configuration;

namespace TaxHi.Infrastructure.Common;

/// <summary>
/// Typed reads of the timeout settings in appsettings.json ("ApiSettings" and "DatabaseSettings"), the single source for
/// request, outbound-HTTP and database-command timeouts. Defaults apply only when a key is absent.
/// </summary>
public static class ApiSettings
{
    /// <summary>Default time an API request may run before it is cut off with 504 REQUEST_TIMEOUT.</summary>
    public static int RequestTimeoutSeconds(IConfiguration c) => c.GetValue("ApiSettings:RequestTimeoutSeconds", 60);

    /// <summary>Longer budget for payment order / verification / cancellation, which wait on the gateway and the database.</summary>
    public static int PaymentRequestTimeoutSeconds(IConfiguration c) => c.GetValue("ApiSettings:PaymentRequestTimeoutSeconds", 120);

    /// <summary>Timeout for outbound calls (Razorpay, Google, SMTP) unless that service's own section sets one.</summary>
    public static int ExternalServiceTimeoutSeconds(IConfiguration c) => c.GetValue("ApiSettings:ExternalServiceTimeoutSeconds", 30);

    /// <summary>Timeout for one outbound HTTP service: its own "{section}:HttpTimeoutSeconds", else the shared default.</summary>
    public static TimeSpan HttpTimeout(IConfiguration c, string section) =>
        TimeSpan.FromSeconds(c.GetValue<int?>($"{section}:HttpTimeoutSeconds") is > 0 and var own ? own : ExternalServiceTimeoutSeconds(c));
}

public static class DatabaseSettings
{
    /// <summary>Default SQL command timeout, applied to every Dapper command (SqlMapper.Settings.CommandTimeout).</summary>
    public static int CommandTimeoutSeconds(IConfiguration c) => c.GetValue("DatabaseSettings:CommandTimeoutSeconds", 120);

    /// <summary>For genuinely long reads (booking history); never shorter than the default.</summary>
    public static int LongCommandTimeoutSeconds(IConfiguration c) => Math.Max(c.GetValue("DatabaseSettings:LongCommandTimeoutSeconds", 300), CommandTimeoutSeconds(c));
}
