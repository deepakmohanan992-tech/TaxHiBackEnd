using TaxHi.Domain.Enums;

namespace TaxHi.Domain.Entities;

public sealed class Location
{
    public Guid Id { get; }
    public string Name { get; }
    public string City { get; }
    public LocationType Type { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    public Location(Guid id, string name, string city, LocationType type, double? latitude = null, double? longitude = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Location name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required.", nameof(city));
        Id = id;
        Name = name.Trim();
        City = city.Trim();
        Type = type;
        Latitude = latitude;
        Longitude = longitude;
    }

    public bool IsSameAs(Location other) => other is not null && Id == other.Id;
}