namespace TaxHi.Infrastructure.Helpers;

internal static class SearchHelper
{
    public static string? NormalizeKeyword(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

    /// <summary>
    /// Converts a list of IDs to a comma-separated string (e.g. "1,2,3") for the stored procedure,
    /// or null when no IDs were supplied.
    /// </summary>
    public static string? ToCsv(IEnumerable<int>? ids)
    {
        var distinct = ids?.Distinct().ToList();
        return distinct is { Count: > 0 } ? string.Join(',', distinct) : null;
    }
}