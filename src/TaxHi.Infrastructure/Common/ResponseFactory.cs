using TaxHi.Application.Models;

namespace TaxHi.Infrastructure.Common;

public static class ResponseFactory
{
    public static CommonResponse<T> Success<T>(T data, string message) =>
        new() { Success = true, StatusCode = 200, Message = message, Data = data };

    public static CommonResponse<T> Failure<T>(int statusCode, string message, string errorCode, int? retryAfter = null) =>
        new()
        {
            Success = false,
            StatusCode = statusCode,
            Message = message,
            ErrorCode = errorCode,
            Data = retryAfter is null ? default : (T)(object)new { RetryAfterSeconds = retryAfter.Value }
        };
}
