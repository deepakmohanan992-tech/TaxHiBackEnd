using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Bookings;
using TaxHi.Application.Features.Drivers;
using TaxHi.Application.Features.FareQuotes;
using TaxHi.Application.Features.Payments;
using TaxHi.Infrastructure.Caching;
using TaxHi.Infrastructure.Messaging;
using TaxHi.Infrastructure.Repositories;
using TaxHi.Infrastructure.Services;

namespace TaxHi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaxHiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PricingOptions>(configuration.GetSection("Pricing"));
        services.AddSingleton<IFareCalculator, DefaultFareCalculator>();

        services.AddMemoryCache();
        services.AddSingleton<ICacheService, InMemoryCacheService>();

        services.AddSingleton<IVehicleCatalog, DevelopmentVehicleCatalog>();
        services.AddSingleton<IRouteProvider, DevelopmentRouteProvider>();
        services.AddSingleton<ILocationService, DevelopmentLocationService>();
        services.AddSingleton<IVehicleAvailabilityService, DevelopmentVehicleAvailabilityService>();

        services.AddSingleton<IFareQuoteStore, InMemoryFareQuoteStore>();
        services.AddSingleton<IBookingStore, InMemoryBookingStore>();
        services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
        services.AddSingleton<IDriverStore, InMemoryDriverStore>();

        services.AddSingleton<IDriverService, DriverService>();
        services.AddSingleton<INotificationService, LoggingNotificationService>();
        services.AddSingleton<IMessagePublisher, InMemoryMessagePublisher>();

        services.AddSingleton<FareQuoteService>();
        services.AddSingleton<BookingService>();
        services.AddSingleton<PaymentService>();

        return services;
    }
}