using TaxHi.Application.Models;

namespace TaxHi.Api.Middleware;

/// <summary>The single error shape every non-controller failure uses: the existing CommonResponse envelope.</summary>
public static class ApiErrorResponse
{
    public static CommonResponse<object> Create(int statusCode, string message, string errorCode) => new()
    {
        Success = false,
        StatusCode = statusCode,
        Message = message,
        ErrorCode = errorCode
    };

    public static async Task WriteAsync(HttpContext context, int statusCode, string message, string errorCode)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(Create(statusCode, message, errorCode));
    }
}
