using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;
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
        services.AddScoped<IAirportTerminalService, AirportTerminalService>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        // Booking:* values are read straight from IConfiguration (see BookingSettings); validated once at startup as before.
        if (BookingSettings.HoldDurationMinutes(configuration) is not (> 0 and <= 1440))
            throw new InvalidOperationException("Booking:HoldDurationMinutes must be between 1 and 1440.");
        if (BookingSettings.LockTimeoutMilliseconds(configuration) is not (>= 0 and <= 120000))
            throw new InvalidOperationException("Booking:LockTimeoutMilliseconds must be between 0 and 120000.");
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IBookingInvoiceRepository, BookingInvoiceRepository>();
        services.AddScoped<IBookingInvoiceService, BookingInvoiceService>();
        services.AddScoped<IBookingEmailRepository, BookingEmailRepository>();
        services.AddScoped<IBookingConfirmationEmailService, BookingConfirmationEmailService>();
        services.AddHttpClient<IPaymentService, PaymentService>();
        services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();
        services.AddScoped<IRouteDistanceService, RouteDistanceService>();
        services.AddScoped<IYardDistanceService, YardDistanceService>();
        services.AddScoped<ITripContextService, TripContextService>();
        services.AddScoped<IBookingRouteRepository, BookingRouteRepository>();
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