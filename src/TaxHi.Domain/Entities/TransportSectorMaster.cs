namespace TaxHi.Domain.Entities;

public sealed class TransportSectorMaster
{
    public int SectorId { get; set; }
    public string SectorName { get; set; } = string.Empty;
    public int CityId { get; set; }
    public int StateId { get; set; }
    public int CountryId { get; set; }
    public bool IsActive { get; set; }
}
