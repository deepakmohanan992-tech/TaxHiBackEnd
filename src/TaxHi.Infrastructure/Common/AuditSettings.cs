using Microsoft.Extensions.Configuration;

namespace TaxHi.Infrastructure.Common;

/// <summary>Typed reads of the "ApiAudit" section of appsettings.json.</summary>
public static class AuditSettings
{
    public static bool Enabled(IConfiguration c) => c.GetValue("ApiAudit:Enabled", true);
    public static int QueueCapacity(IConfiguration c) => Math.Clamp(c.GetValue("ApiAudit:QueueCapacity", 5000), 100, 100_000);
    public static int MaxRequestBodyBytes(IConfiguration c) => Math.Clamp(c.GetValue("ApiAudit:MaxRequestBodyBytes", 16_384), 0, 1_048_576);
    public static int MaxResponseBodyBytes(IConfiguration c) => Math.Clamp(c.GetValue("ApiAudit:MaxResponseBodyBytes", 16_384), 0, 1_048_576);
    public static int WriteTimeoutSeconds(IConfiguration c) => Math.Clamp(c.GetValue("ApiAudit:WriteTimeoutSeconds", 15), 1, 120);

    /// <summary>Path prefixes that are not audited (health probes, swagger).</summary>
    public static string[] ExcludedPaths(IConfiguration c) =>
        c.GetSection("ApiAudit:ExcludedPaths").Get<string[]>() is { Length: > 0 } p ? p : ["/health", "/swagger"];
}
