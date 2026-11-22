namespace TaxHi.Infrastructure.Models;

/// <summary>Second result set of USP_GetUserBookings: pagination metadata.</summary>
internal sealed class BookingListMetadata
{
    public string? Mode { get; init; }
    public int CurrentPage { get; init; }
    public int PageSize { get; init; }
    public int TotalRecords { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>Third result set of USP_GetUserBookings: tab badge counts.</summary>
internal sealed class BookingTabCounts
{
    public int UpcomingCount { get; init; }
    public int CurrentCount { get; init; }
    public int HistoryCount { get; init; }
}
