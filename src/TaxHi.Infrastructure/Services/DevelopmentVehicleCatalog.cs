using TaxHi.Application.Interfaces;
using TaxHi.Domain.Entities;
using TaxHi.Infrastructure.DevelopmentData;

namespace TaxHi.Infrastructure.Services;

public sealed class DevelopmentVehicleCatalog : IVehicleCatalog
{
    public IReadOnlyList<VehicleCategory> GetAll() => SampleVehicles.All;

    public VehicleCategory? GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToLowerInvariant();
        return SampleVehicles.All.FirstOrDefault(v => v.Code == c);
    }
}