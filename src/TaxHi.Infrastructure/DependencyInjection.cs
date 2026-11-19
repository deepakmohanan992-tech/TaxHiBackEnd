using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Repositories;
using TaxHi.Infrastructure.Services;

namespace TaxHi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaxHiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection("Booking"))
            .Validate(o => o.HoldDurationMinutes is > 0 and <= 1440, "Booking:HoldDurationMinutes must be between 1 and 1440.")
            .Validate(o => o.LockTimeoutMilliseconds is >= 0 and <= 120000, "Booking:LockTimeoutMilliseconds must be between 0 and 120000.")
            .ValidateOnStart();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IBookingInvoiceRepository, BookingInvoiceRepository>();
        services.AddScoped<IBookingInvoiceService, BookingInvoiceService>();
        services.AddOptions<RazorpayOptions>()
            .Bind(configuration.GetSection("Razorpay"))
            .Validate(o => o.Environment is "Test" or "Live", "Razorpay:Environment must be 'Test' or 'Live'.")
            .Validate(o => string.IsNullOrEmpty(o.KeyId)
                    || o.KeyId.StartsWith(o.Environment == "Live" ? "rzp_live_" : "rzp_test_", StringComparison.Ordinal),
                "Razorpay:KeyId does not match Razorpay:Environment.")
            .Validate(o => Uri.TryCreate(o.ApiBaseUrl, UriKind.Absolute, out _), "Razorpay:ApiBaseUrl must be an absolute URL.")
            .ValidateOnStart();
        services.AddHttpClient<IPaymentService, PaymentService>((provider, client) =>
        {
            var razorpay = provider.GetRequiredService<IOptions<RazorpayOptions>>().Value;
            client.BaseAddress = new Uri(razorpay.ApiBaseUrl.EndsWith('/') ? razorpay.ApiBaseUrl : razorpay.ApiBaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(razorpay.HttpTimeoutSeconds);
            if (!string.IsNullOrEmpty(razorpay.KeyId))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{razorpay.KeyId}:{razorpay.KeySecret}")));
        });
        services.AddSingleton<IOtpNotifier, NullOtpNotifier>();
        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection("Authentication"))
            .Validate(options => options.JwtSigningKey.Length >= 32, "Authentication:JwtSigningKey must contain at least 32 characters.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.JwtIssuer), "Authentication:JwtIssuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.JwtAudience), "Authentication:JwtAudience is required.")
            .ValidateOnStart();

        return services;
    }
}