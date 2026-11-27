using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Api.Middleware;

/// <summary>How an unhandled exception is presented to the client. Messages are safe to show; details go to the log and the audit table.</summary>
public readonly record struct MappedError(int StatusCode, string ErrorCode, string Message, bool IsTimeout);

public static class ExceptionMapping
{
    public const string TimeoutMessage = "The request took too long to complete. Please try again.";

    public static MappedError Map(Exception ex) => ex switch
    {
        SqlException sql when SqlErrors.IsTimeout(sql) =>
            new(StatusCodes.Status504GatewayTimeout, "DATABASE_TIMEOUT", TimeoutMessage, true),
        SqlException { Number: 1205 } =>
            new(StatusCodes.Status409Conflict, "DATABASE_BUSY", "The system is busy. Please try again in a moment.", false),
        // THROW 5xxxx raised by our own stored procedures carries a message written for the customer.
        SqlException { Number: >= 50000 } sql =>
            new(StatusCodes.Status409Conflict, "BUSINESS_RULE_VIOLATION", sql.Message, false),
        SqlException =>
            new(StatusCodes.Status500InternalServerError, "DATABASE_ERROR", "A database error occurred. Please try again.", false),
        TimeoutException =>
            new(StatusCodes.Status504GatewayTimeout, "REQUEST_TIMEOUT", TimeoutMessage, true),
        TaskCanceledException { InnerException: TimeoutException } =>
            new(StatusCodes.Status504GatewayTimeout, "REQUEST_TIMEOUT", TimeoutMessage, true),
        BadHttpRequestException bad =>
            new(bad.StatusCode is >= 400 and < 500 ? bad.StatusCode : StatusCodes.Status400BadRequest, "BAD_REQUEST", "The request is invalid or too large.", false),
        ArgumentException arg =>
            new(StatusCodes.Status400BadRequest, "BAD_REQUEST", arg.Message, false),
        UnauthorizedAccessException =>
            new(StatusCodes.Status403Forbidden, "FORBIDDEN", "You do not have permission to perform this action.", false),
        KeyNotFoundException =>
            new(StatusCodes.Status404NotFound, "NOT_FOUND", "The requested resource was not found.", false),
        InvalidOperationException =>
            new(StatusCodes.Status409Conflict, "INVALID_OPERATION", "The request could not be completed in the current state.", false),
        _ =>
            new(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred. Please try again.", false)
    };
}
