using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaxHi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaxHiInfrastructure(this IServiceCollection services, IConfiguration _) => services;
}