using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Api.Middleware;

/// <summary>
/// Writes exactly one audit record per API request, success or failure. It sits OUTSIDE the exception middleware, so it
/// sees the final status code and the exception that middleware handled (via HttpContext.Items), and it never throws:
/// the record is only handed to an in-memory queue; persisting it is a background job.
/// The response body is copied (not buffered) while it streams to the client, so capture cannot delay or alter the response.
/// </summary>
public sealed class ApiAuditMiddleware
{
    private const int MaxStackTraceChars = 20_000;

    private readonly RequestDelegate _next;
    private readonly IApiAuditQueue _queue;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ApiAuditMiddleware> _logger;
    private readonly bool _enabled;
    private readonly int _maxRequestBytes;
    private readonly int _maxResponseBytes;
    private readonly string[] _excludedPaths;

    public ApiAuditMiddleware(RequestDelegate next, IApiAuditQueue queue, IConfiguration configuration,
        IHostEnvironment environment, ILogger<ApiAuditMiddleware> logger)
    {
        _next = next;
        _queue = queue;
        _environment = environment;
        _logger = logger;
        _enabled = AuditSettings.Enabled(configuration);
        _maxRequestBytes = AuditSettings.MaxRequestBodyBytes(configuration);
        _maxResponseBytes = AuditSettings.MaxResponseBodyBytes(configuration);
        _excludedPaths = AuditSettings.ExcludedPaths(configuration);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || _excludedPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var receivedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string? requestBody = null;
        try { requestBody = await ReadRequestBodyAsync(context.Request); }
        catch (Exception ex) { _logger.LogDebug(ex, "Request body could not be captured for auditing."); }

        var originalBody = context.Response.Body;
        var tee = new CapturingStream(originalBody, _maxResponseBytes);
        context.Response.Body = tee;
        Exception? thrown = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            thrown = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            context.Response.Body = originalBody;
            try
            {
                _queue.TryEnqueue(Build(context, receivedAt, stopwatch.ElapsedMilliseconds, requestBody, tee, thrown));
            }
            catch (Exception ex)
            {
                // An audit problem is never allowed to change the API outcome.
                _logger.LogWarning(ex, "API audit record could not be built or queued.");
            }
        }
    }

    // ------------------------------------------------------------------ request body

    private async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (_maxRequestBytes <= 0 || !HasJsonBody(request)) return null;
        if (request.ContentLength > _maxRequestBytes)
            return $"[request body larger than {_maxRequestBytes} bytes not stored]";

        request.EnableBuffering();
        var buffer = new byte[_maxRequestBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await request.Body.ReadAsync(buffer.AsMemory(read))) > 0)
            read += n;
        request.Body.Position = 0; // the controller (or the webhook signature check) reads it again

        if (read > _maxRequestBytes) return $"[request body larger than {_maxRequestBytes} bytes not stored]";
        return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static bool HasJsonBody(HttpRequest request) =>
        (request.ContentLength is null or > 0)
        && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
        && request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    // ------------------------------------------------------------------ record

    private ApiAuditRecord Build(HttpContext context, DateTime receivedAt, long elapsedMs, string? requestBody,
        CapturingStream response, Exception? thrown)
    {
        var request = context.Request;
        var exception = thrown ?? context.Items[ApiAuditContextKeys.Exception] as Exception;
        var status = thrown is not null && !context.Response.HasStarted && context.Response.StatusCode < 400
            ? StatusCodes.Status500InternalServerError
            : context.Response.StatusCode;

        var isJson = context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        var rawResponse = isJson ? response.CapturedText() : null;
        var (errorCode, errorMessage) = status >= 400 ? ReadError(rawResponse) : (null, null);

        var timedOut = context.Items[ApiAuditContextKeys.TimedOut] is true
            || status is StatusCodes.Status504GatewayTimeout or StatusCodes.Status408RequestTimeout
            || exception is TimeoutException;

        var descriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var userContext = context.RequestServices.GetService<IUserContext>();

        return new ApiAuditRecord
        {
            CorrelationId = Cut(request.Headers["X-Correlation-ID"].FirstOrDefault(), 64) ?? context.TraceIdentifier,
            RequestId = Cut(context.TraceIdentifier, 64),
            HttpMethod = Cut(request.Method, 10) ?? "UNKNOWN",
            ApiEndpoint = Cut(request.Path.Value, 300) ?? "/",
            ControllerName = Cut(descriptor?.ControllerName, 100),
            ActionName = Cut(descriptor?.ActionName, 100),
            RequestUrl = AuditRedactor.RedactUrl(request.Path.Value ?? "/", request.QueryString.Value),
            RequestPayload = AuditRedactor.RedactJson(requestBody, _maxRequestBytes),
            RequestHeaders = AuditRedactor.SafeHeaderText(request.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToString()))),
            UserId = userContext?.UserId,
            VendorId = userContext?.VendorId,
            ClientIpAddress = Cut(context.Connection.RemoteIpAddress?.ToString(), 45),
            UserAgent = Cut(request.Headers.UserAgent.ToString(), 500),
            RequestReceivedAt = receivedAt,
            HttpStatusCode = status,
            ResponsePayload = AuditRedactor.RedactJson(rawResponse, _maxResponseBytes),
            ResponseReceivedAt = receivedAt.AddMilliseconds(elapsedMs),
            ExecutionDurationMs = (int)Math.Min(elapsedMs, int.MaxValue),
            ApiStatus = timedOut ? ApiAuditStatus.Timeout : status >= 400 || exception is not null ? ApiAuditStatus.Failed : ApiAuditStatus.Success,
            ErrorCode = Cut(errorCode ?? (exception is null ? null : ExceptionMapping.Map(exception).ErrorCode), 100),
            ErrorMessage = Cut(errorMessage, 1000),
            ExceptionType = Cut(exception?.GetType().FullName, 300),
            ExceptionDetails = exception is null ? null : Cut(Describe(exception), 4000),
            StackTrace = exception?.StackTrace is { } trace ? AuditRedactor.Truncate(trace, MaxStackTraceChars) : null,
            Environment = Cut(_environment.EnvironmentName, 30)
        };
    }

    /// <summary>errorCode / message of the CommonResponse envelope (or a ProblemDetails) the client received.</summary>
    private static (string? Code, string? Message) ReadError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (Str(root, "errorCode"), Str(root, "message") ?? Str(root, "detail") ?? Str(root, "title"));
        }
        catch (JsonException) { return (null, null); }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder(ex.Message);
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            sb.Append(" -> ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
        return sb.ToString();
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    // ------------------------------------------------------------------ response capture

    /// <summary>Write-through stream that keeps a copy of the first <c>limit</c> bytes. It never buffers the response.</summary>
    private sealed class CapturingStream(Stream inner, int limit) : Stream
    {
        private readonly MemoryStream _copy = new();
        public string CapturedText() => Encoding.UTF8.GetString(_copy.GetBuffer(), 0, (int)_copy.Length);

        private void Capture(ReadOnlySpan<byte> data)
        {
            var room = limit - (int)_copy.Length;
            if (room > 0) _copy.Write(data[..Math.Min(room, data.Length)]);
        }

        public override void Write(byte[] buffer, int offset, int count) { Capture(buffer.AsSpan(offset, count)); inner.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Capture(buffer); inner.Write(buffer); }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) { Capture(buffer.AsSpan(offset, count)); await inner.WriteAsync(buffer.AsMemory(offset, count), ct); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Capture(buffer.Span); await inner.WriteAsync(buffer, ct); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
