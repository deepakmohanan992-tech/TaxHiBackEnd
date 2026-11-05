namespace TaxHi.Domain.Entities;

public sealed class CityMaster
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StateId { get; set; }
    public int? HobseCityId { get; set; }
    public string? PromptKeywords { get; set; }
}
