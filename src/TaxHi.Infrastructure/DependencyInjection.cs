using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<ILookupRepository, LookupRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
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