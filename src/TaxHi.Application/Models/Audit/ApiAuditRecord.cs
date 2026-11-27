namespace TaxHi.Application.Models.Audit;

public static class ApiAuditStatus
{
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Timeout = "Timeout";
}

/// <summary>Keys of <c>HttpContext.Items</c> shared by the exception and audit middleware so one request yields one audit row.</summary>
public static class ApiAuditContextKeys
{
    /// <summary>The exception the exception middleware handled for this request (already logged once there).</summary>
    public const string Exception = "TaxHi.Audit.Exception";
    /// <summary>Set when the request was cut off by the request timeout or a database/HTTP timeout.</summary>
    public const string TimedOut = "TaxHi.Audit.TimedOut";
}

/// <summary>One audited API request. Payloads are already redacted and size-capped.</summary>
public sealed class ApiAuditRecord
{
    public string CorrelationId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = string.Empty;
    public string? ControllerName { get; set; }
    public string? ActionName { get; set; }
    public string? RequestUrl { get; set; }
    public string? RequestPayload { get; set; }
    public string? RequestHeaders { get; set; }
    public long? UserId { get; set; }
    public int? VendorId { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime RequestReceivedAt { get; set; }

    public int? HttpStatusCode { get; set; }
    public string? ResponsePayload { get; set; }
    public DateTime? ResponseReceivedAt { get; set; }
    public int? ExecutionDurationMs { get; set; }
    public string ApiStatus { get; set; } = ApiAuditStatus.Success;

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionDetails { get; set; }
    public string? StackTrace { get; set; }

    public string? Environment { get; set; }
}
