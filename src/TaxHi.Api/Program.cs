using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using TaxHi.Api.Middleware;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Services;
using TaxHi.Infrastructure;
using TaxHi.Infrastructure.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    // Model-validation failures use the same CommonResponse envelope as every other error, with the first
    // problem as the message (so the UI can show it) and the per-field messages in Data.
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors
                    .Select(x => e.Key.StartsWith('$') || string.IsNullOrWhiteSpace(x.ErrorMessage) ? "The request body is invalid." : x.ErrorMessage)
                    .ToArray());
        var first = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "The request is invalid.";
        return new BadRequestObjectResult(new CommonResponse<Dictionary<string, string[]>>
        {
            Success = false,
            StatusCode = StatusCodes.Status400BadRequest,
            Message = first,
            ErrorCode = "VALIDATION_FAILED",
            Data = errors
        });
    });

// One request-timeout policy for the API (ApiSettings in appsettings.json). Payment endpoints use the longer "payment" policy;
// the Razorpay webhook has no timeout so a delivery that already changed the database is never cut off half-way.
builder.Services.AddRequestTimeouts(options =>
{
    static Task WriteTimeout(HttpContext context)
    {
        context.Items[ApiAuditContextKeys.TimedOut] = true;
        return ApiErrorResponse.WriteAsync(context, StatusCodes.Status504GatewayTimeout, ExceptionMapping.TimeoutMessage, "REQUEST_TIMEOUT");
    }

    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(ApiSettings.RequestTimeoutSeconds(builder.Configuration)),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        WriteTimeoutResponse = WriteTimeout
    };
    options.AddPolicy("payment", new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(ApiSettings.PaymentRequestTimeoutSeconds(builder.Configuration)),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        WriteTimeoutResponse = WriteTimeout
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
var authenticationOptions = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>()
    ?? throw new InvalidOperationException("Authentication configuration is required.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authenticationOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = authenticationOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authenticationOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // 401 / 403 carry the standard error envelope instead of an empty body.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ApiErrorResponse.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "Authentication is required or your session has expired. Please sign in again.", "UNAUTHORIZED");
            },
            OnForbidden = context => ApiErrorResponse.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                "You do not have permission to perform this action.", "FORBIDDEN")
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) => new ValueTask(ApiErrorResponse.WriteAsync(context.HttpContext,
        StatusCodes.Status429TooManyRequests, "Too many requests. Please wait a moment and try again.", "TOO_MANY_REQUESTS"));
    options.AddPolicy("otp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("maps", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddTaxHiInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
// Order matters: correlation id first; the audit middleware wraps the exception middleware so it records the final
// status and the handled exception exactly once; request timeouts sit inside both so a timeout is handled, not thrown.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiAuditMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRequestTimeouts();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
