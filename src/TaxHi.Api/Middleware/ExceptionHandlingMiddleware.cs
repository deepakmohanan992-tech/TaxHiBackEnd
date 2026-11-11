using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace TaxHi.Api.Middleware;

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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (SqlException ex) when (IsSqlCancellation(ex, context))
        {
            _logger.LogDebug("SQL operation for request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (ArgumentException ex)
        {
            await WriteProblem(context, StatusCodes.Status400BadRequest, "Bad request", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await WriteProblem(context, StatusCodes.Status409Conflict, "Invalid operation", ex.Message);
        }
        catch (Exception ex)
        {
            if (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogDebug("Request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
                context.Response.StatusCode = 499;
                return;
            }

            _logger.LogError(ex, "Unhandled exception");
            await WriteProblem(context, StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.");
        }
    }

    private static bool IsSqlCancellation(SqlException ex, HttpContext context)
    {
        return context.RequestAborted.IsCancellationRequested ||
               ex.Message.Contains("Operation cancelled by user", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteProblem(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        await context.Response.WriteAsJsonAsync(problem);
    }
}