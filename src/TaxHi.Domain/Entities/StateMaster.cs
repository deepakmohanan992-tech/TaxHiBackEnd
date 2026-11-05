namespace TaxHi.Domain.Entities;

public sealed class StateMaster
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CountryId { get; set; }
}
