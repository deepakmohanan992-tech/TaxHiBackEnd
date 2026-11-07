namespace TaxHi.Application.Models;

public class PaginationRequestDto
{
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Keyword { get; init; }
}
