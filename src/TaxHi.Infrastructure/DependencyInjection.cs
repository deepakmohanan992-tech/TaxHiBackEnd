using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Repositories;

namespace TaxHi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaxHiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));
        services.AddScoped<ISearchRepository, SearchRepository>();

        return services;
    }
}