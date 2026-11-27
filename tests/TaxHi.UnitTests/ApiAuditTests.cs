using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxHi.Api.Middleware;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.UnitTests;

public class AuditRedactorTests
{
    [Fact]
    public void Secrets_are_replaced_at_every_depth()
    {
        const string json = """
        {"mobile":"9876543210","email":"jane@example.com","password":"p@ss","otp":"123456","accessToken":"abc","refresh_token":"def",
         "razorpay_signature":"sig","cardNumber":"4111111111111111","cvv":"123","KeySecret":"s3",
         "nested":{"authorization":"Bearer x","items":[{"token":"t"},{"name":"ok"}]},"company":"Acme","keyId":"rzp_test_1","amount":100}
        """;

        var result = AuditRedactor.RedactJson(json, 10_000)!;
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        foreach (var key in new[] { "password", "otp", "accessToken", "refresh_token", "razorpay_signature", "cardNumber", "cvv", "KeySecret" })
            Assert.Equal(AuditRedactor.Redacted, root.GetProperty(key).GetString());
        Assert.Equal(AuditRedactor.Redacted, root.GetProperty("nested").GetProperty("authorization").GetString());
        Assert.Equal(AuditRedactor.Redacted, root.GetProperty("nested").GetProperty("items")[0].GetProperty("token").GetString());
        Assert.Equal("ok", root.GetProperty("nested").GetProperty("items")[1].GetProperty("name").GetString());

        // ordinary fields survive ("company" contains "pan" but is not a card number; keyId is public)
        Assert.Equal("Acme", root.GetProperty("company").GetString());
        Assert.Equal("rzp_test_1", root.GetProperty("keyId").GetString());
        Assert.Equal(100, root.GetProperty("amount").GetInt32());

        // personal data is masked, not stored in full
        Assert.Equal("********10", root.GetProperty("mobile").GetString());
        Assert.Equal("j***@example.com", root.GetProperty("email").GetString());
        Assert.DoesNotContain("p@ss", result);
        Assert.DoesNotContain("4111111111111111", result);
    }

    [Fact]
    public void Non_json_is_not_stored_and_empty_is_null()
    {
        Assert.Equal(AuditRedactor.NotStored, AuditRedactor.RedactJson("password=abc&x=1", 1000));
        Assert.Null(AuditRedactor.RedactJson("   ", 1000));
        Assert.Null(AuditRedactor.RedactJson("{}", 0));
    }

    [Fact]
    public void Payload_is_capped()
    {
        var result = AuditRedactor.RedactJson("{\"note\":\"" + new string('x', 5000) + "\"}", 200)!;
        Assert.True(result.Length <= 200);
        Assert.EndsWith("...[truncated]", result);
    }

    [Fact]
    public void Query_string_secrets_are_redacted()
    {
        var url = AuditRedactor.RedactUrl("/api/x", "?bookingId=5&token=abc123&otp=999999&vendorId=3");
        Assert.Contains("bookingId=5", url);
        Assert.Contains("vendorId=3", url);
        Assert.DoesNotContain("abc123", url);
        Assert.DoesNotContain("999999", url);
    }

    [Fact]
    public void Only_safe_headers_are_kept()
    {
        var text = AuditRedactor.SafeHeaderText(new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret", ["Cookie"] = "s=1", ["X-Razorpay-Signature"] = "sig",
            ["Content-Type"] = "application/json", ["Origin"] = "http://localhost:5173"
        })!;

        Assert.Contains("Content-Type: application/json", text);
        Assert.Contains("Origin: http://localhost:5173", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("Cookie", text);
        Assert.DoesNotContain("sig", text);
    }
}

public class ApiAuditPipelineTests
{
    private sealed class Queue(bool throws = false) : IApiAuditQueue
    {
        public List<ApiAuditRecord> Records = [];
        public bool TryEnqueue(ApiAuditRecord record)
        {
            if (throws) throw new InvalidOperationException("audit queue is broken");
            Records.Add(record);
            return true;
        }
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "TaxHi";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config => new ConfigurationBuilder().Build();

    /// <summary>correlation -> audit -> exception handling -> endpoint, the same order as Program.cs.</summary>
    private static async Task<(DefaultHttpContext Context, string Body)> RunAsync(Queue queue, RequestDelegate endpoint,
        string method = "POST", string path = "/api/Booking/SaveBookingPayment", string? requestJson = null, string query = "")
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Headers["X-Correlation-ID"] = "corr-123";
        context.Request.Headers.Authorization = "Bearer super-secret";
        if (requestJson is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(requestJson);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
        }
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var exceptions = new ExceptionHandlingMiddleware(endpoint, NullLogger<ExceptionHandlingMiddleware>.Instance);
        var audit = new ApiAuditMiddleware(exceptions.InvokeAsync, queue, Config, new Env(), NullLogger<ApiAuditMiddleware>.Instance);
        await audit.InvokeAsync(context);

        return (context, Encoding.UTF8.GetString(responseBody.ToArray()));
    }

    private static Task Json(HttpContext c, int status, object body)
    {
        c.Response.StatusCode = status;
        return c.Response.WriteAsJsonAsync(body);
    }

    [Fact]
    public async Task Success_is_audited_once_with_redacted_payloads()
    {
        var queue = new Queue();
        var (context, body) = await RunAsync(queue,
            c => Json(c, 200, new { success = true, message = "ok", data = new { accessToken = "jwt-abc", bookingId = 5 } }),
            requestJson: "{\"otp\":\"123456\",\"bookingId\":5}", query: "?token=zzz&x=1");

        var record = Assert.Single(queue.Records);
        Assert.Equal(200, record.HttpStatusCode);
        Assert.Equal(ApiAuditStatus.Success, record.ApiStatus);
        Assert.Equal("corr-123", record.CorrelationId);
        Assert.Equal("POST", record.HttpMethod);
        Assert.Equal("/api/Booking/SaveBookingPayment", record.ApiEndpoint);
        Assert.Equal("Testing", record.Environment);
        Assert.NotNull(record.ExecutionDurationMs);

        Assert.DoesNotContain("123456", record.RequestPayload);
        Assert.Contains("bookingId", record.RequestPayload);
        Assert.DoesNotContain("jwt-abc", record.ResponsePayload);
        Assert.DoesNotContain("zzz", record.RequestUrl);
        Assert.DoesNotContain("super-secret", record.RequestHeaders ?? "");

        // the client still receives the complete, untouched response
        Assert.Contains("jwt-abc", body);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Bad_request_is_audited_as_failed_with_the_error_code()
    {
        var queue = new Queue();
        await RunAsync(queue, c => Json(c, 400, new { success = false, message = "BookingId is required.", errorCode = "INVALID_BOOKING_ID" }));

        var record = Assert.Single(queue.Records);
        Assert.Equal(400, record.HttpStatusCode);
        Assert.Equal(ApiAuditStatus.Failed, record.ApiStatus);
        Assert.Equal("INVALID_BOOKING_ID", record.ErrorCode);
        Assert.Equal("BookingId is required.", record.ErrorMessage);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Unauthorized_and_forbidden_are_audited(int status)
    {
        var queue = new Queue();
        await RunAsync(queue, c => Json(c, status, new { success = false, message = "no", errorCode = "UNAUTHORIZED" }));

        var record = Assert.Single(queue.Records);
        Assert.Equal(status, record.HttpStatusCode);
        Assert.Equal(ApiAuditStatus.Failed, record.ApiStatus);
    }

    [Fact]
    public async Task Unhandled_exception_returns_the_standard_envelope_and_is_audited_once_with_details()
    {
        var queue = new Queue();
        var (context, body) = await RunAsync(queue, _ => throw new InvalidProgramException("secret internals: server=db01"));

        Assert.Equal(500, context.Response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("INTERNAL_ERROR", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("db01", body);                       // nothing internal reaches the client

        var record = Assert.Single(queue.Records);                 // exception middleware + audit middleware: still one row
        Assert.Equal(500, record.HttpStatusCode);
        Assert.Equal(ApiAuditStatus.Failed, record.ApiStatus);
        Assert.Equal("System.InvalidProgramException", record.ExceptionType);
        Assert.Contains("db01", record.ExceptionDetails);          // developers get the detail
        Assert.False(string.IsNullOrEmpty(record.ErrorCode));
    }

    [Fact]
    public async Task Timeout_is_audited_with_the_timeout_status()
    {
        var queue = new Queue();
        var (context, body) = await RunAsync(queue, _ => throw new TimeoutException("db took too long"));

        Assert.Equal(504, context.Response.StatusCode);
        Assert.Contains("REQUEST_TIMEOUT", body);
        var record = Assert.Single(queue.Records);
        Assert.Equal(ApiAuditStatus.Timeout, record.ApiStatus);
        Assert.Equal(504, record.HttpStatusCode);
    }

    [Fact]
    public async Task Request_timeout_middleware_flag_marks_the_row_as_timeout()
    {
        var queue = new Queue();
        await RunAsync(queue, c =>
        {
            c.Items[ApiAuditContextKeys.TimedOut] = true;
            return Json(c, 504, new { success = false, message = "slow", errorCode = "REQUEST_TIMEOUT" });
        });

        Assert.Equal(ApiAuditStatus.Timeout, Assert.Single(queue.Records).ApiStatus);
    }

    [Fact]
    public async Task A_broken_audit_queue_never_changes_the_api_response()
    {
        var (context, body) = await RunAsync(new Queue(throws: true), c => Json(c, 200, new { success = true, message = "fine" }));

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Contains("fine", body);
    }

    [Fact]
    public async Task The_request_body_is_still_readable_by_the_endpoint()
    {
        string? seen = null;
        await RunAsync(new Queue(), async c =>
        {
            seen = await new StreamReader(c.Request.Body).ReadToEndAsync();
            await Json(c, 200, new { success = true });
        }, requestJson: "{\"a\":1}");

        Assert.Equal("{\"a\":1}", seen);
    }

    [Fact]
    public async Task Client_abort_is_reported_as_499_and_audited()
    {
        var queue = new Queue();
        using var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.RequestAborted = cts.Token;

        var exceptions = new ExceptionHandlingMiddleware(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await new ApiAuditMiddleware(exceptions.InvokeAsync, queue, Config, new Env(), NullLogger<ApiAuditMiddleware>.Instance).InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(499, Assert.Single(queue.Records).HttpStatusCode);
    }
}

public class ExceptionMappingTests
{
    [Fact]
    public void Common_exceptions_map_to_consistent_status_codes()
    {
        Assert.Equal(400, ExceptionMapping.Map(new ArgumentException("bad")).StatusCode);
        Assert.Equal(404, ExceptionMapping.Map(new KeyNotFoundException()).StatusCode);
        Assert.Equal(403, ExceptionMapping.Map(new UnauthorizedAccessException()).StatusCode);
        Assert.Equal(409, ExceptionMapping.Map(new InvalidOperationException("internal state x")).StatusCode);
        Assert.Equal(504, ExceptionMapping.Map(new TimeoutException()).StatusCode);
        Assert.True(ExceptionMapping.Map(new TimeoutException()).IsTimeout);
        Assert.Equal(500, ExceptionMapping.Map(new Exception("boom")).StatusCode);
    }

    [Fact]
    public void Internal_messages_are_not_exposed()
    {
        Assert.DoesNotContain("internal state x", ExceptionMapping.Map(new InvalidOperationException("internal state x")).Message);
        Assert.DoesNotContain("boom", ExceptionMapping.Map(new Exception("boom")).Message);
        Assert.Equal("bad input", ExceptionMapping.Map(new ArgumentException("bad input")).Message); // argument errors are written for the caller
    }
}

public class TimeoutSettingsTests
{
    private static IConfiguration Cfg(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        var c = Cfg();
        Assert.Equal(60, ApiSettings.RequestTimeoutSeconds(c));
        Assert.Equal(120, ApiSettings.PaymentRequestTimeoutSeconds(c));
        Assert.Equal(120, DatabaseSettings.CommandTimeoutSeconds(c));
        Assert.Equal(TimeSpan.FromSeconds(30), ApiSettings.HttpTimeout(c, "Razorpay"));
    }

    [Fact]
    public void Configured_values_win_and_a_service_section_overrides_the_shared_http_default()
    {
        var c = Cfg(("ApiSettings:RequestTimeoutSeconds", "15"), ("ApiSettings:ExternalServiceTimeoutSeconds", "45"),
            ("Razorpay:HttpTimeoutSeconds", "20"), ("DatabaseSettings:CommandTimeoutSeconds", "90"));

        Assert.Equal(15, ApiSettings.RequestTimeoutSeconds(c));
        Assert.Equal(TimeSpan.FromSeconds(20), ApiSettings.HttpTimeout(c, "Razorpay"));
        Assert.Equal(TimeSpan.FromSeconds(45), ApiSettings.HttpTimeout(c, "GoogleMaps"));
        Assert.Equal(90, DatabaseSettings.CommandTimeoutSeconds(c));
    }

    [Fact]
    public void Long_command_timeout_is_never_shorter_than_the_default()
    {
        var c = Cfg(("DatabaseSettings:CommandTimeoutSeconds", "200"), ("DatabaseSettings:LongCommandTimeoutSeconds", "100"));
        Assert.Equal(200, DatabaseSettings.LongCommandTimeoutSeconds(c));
    }
}
