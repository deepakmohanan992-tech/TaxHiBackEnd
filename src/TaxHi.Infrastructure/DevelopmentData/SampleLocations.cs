using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Infrastructure.DevelopmentData;

public static class SampleLocations
{
    public static IReadOnlyList<Location> All { get; } = new List<Location>
    {
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Kochi", "Kochi", LocationType.City, 9.9312, 76.2673),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Coimbatore", "Coimbatore", LocationType.City, 11.0168, 76.9558),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Bangalore", "Bangalore", LocationType.City, 12.9716, 77.5946),
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Chennai", "Chennai", LocationType.City, 13.0827, 80.2707),
        new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Trivandrum", "Thiruvananthapuram", LocationType.City, 8.5241, 76.9366),
        new(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Cochin International Airport", "Kochi", LocationType.Airport, 10.1520, 76.4019),
        new(Guid.Parse("77777777-7777-7777-7777-777777777777"), "Bangalore Airport", "Bangalore", LocationType.Airport, 13.1986, 77.7066),
        new(Guid.Parse("88888888-8888-8888-8888-888888888888"), "Chennai Airport", "Chennai", LocationType.Airport, 12.9941, 80.1709),
    }.AsReadOnly();
}