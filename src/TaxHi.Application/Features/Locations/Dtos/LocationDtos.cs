namespace TaxHi.Application.Features.Locations.Dtos;

public sealed record LocationDto(Guid Id, string Name, string City, string Type, double? Latitude, double? Longitude);

public sealed record LocationSearchResponse(IReadOnlyList<LocationDto> Items, bool IsDevelopmentData, string? Source);