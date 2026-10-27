using TaxHi.Domain.Entities;

namespace TaxHi.Infrastructure.DevelopmentData;

public static class SampleVehicles
{
    public static IReadOnlyList<VehicleCategory> All { get; } = new List<VehicleCategory>
    {
        new("hatchback", "Hatchback", 4, 2, new[] { "AC", "Music System" }),
        new("sedan", "Sedan", 4, 3, new[] { "AC", "Music System", "Comfortable Seats" }),
        new("suv", "SUV", 6, 4, new[] { "AC", "Music System", "Spacious" }),
        new("innova", "Innova", 7, 5, new[] { "AC", "Music System", "Large Luggage" }),
    }.AsReadOnly();
}