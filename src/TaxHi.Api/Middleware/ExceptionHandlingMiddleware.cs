using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Data.SqlClient;
using TaxHi.Application.Models.Audit;

namespace TaxHi.Api.Middleware;

/// <summary>
/// The one place unhandled exceptions are turned into a response and logged. The response is the standard
/// CommonResponse envelope; the exception itself is parked in HttpContext.Items for the audit middleware (which
/// stores the details), so nothing is logged or audited twice and nothing internal reaches the client.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested && !TimedOut(context))
        {
            _logger.LogDebug("Request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (SqlException ex) when (context.RequestAborted.IsCancellationRequested && !TimedOut(context)
            && ex.Message.Contains("Operation cancelled by user", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("SQL operation for request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        // A request cut off by the request timeout surfaces as a cancellation: report it as the timeout it was.
        var mapped = ex is OperationCanceledException && TimedOut(context)
            ? new MappedError(StatusCodes.Status504GatewayTimeout, "REQUEST_TIMEOUT", ExceptionMapping.TimeoutMessage, true)
            : ExceptionMapping.Map(ex);

        context.Items[ApiAuditContextKeys.Exception] = ex;
        if (mapped.IsTimeout) context.Items[ApiAuditContextKeys.TimedOut] = true;

        if (mapped.StatusCode >= 500)
            _logger.LogError(ex, "Unhandled exception ({ErrorCode}) for {Method} {Path}", mapped.ErrorCode, context.Request.Method, context.Request.Path);
        else
            _logger.LogWarning("Request {Method} {Path} failed with {StatusCode} {ErrorCode}: {Message}",
                context.Request.Method, context.Request.Path, mapped.StatusCode, mapped.ErrorCode, ex.Message);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("The response had already started; the error could not be written for {Path}.", context.Request.Path);
            return;
        }

        await ApiErrorResponse.WriteAsync(context, mapped.StatusCode, mapped.Message, mapped.ErrorCode);
    }

    private static bool TimedOut(HttpContext context) =>
        context.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true;
}
