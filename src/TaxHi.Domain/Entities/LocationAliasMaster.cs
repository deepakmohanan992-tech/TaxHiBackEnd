namespace TaxHi.Domain.Entities;

public sealed class LocationAliasMaster
{
    public int LocationAliasId { get; set; }
    public int CityId { get; set; }
    public string? CityName { get; set; }
    public string? AliasName { get; set; }
}
