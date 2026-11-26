using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Services;

public sealed class AirportTerminalService(ISearchRepository searchRepository, ILogger<AirportTerminalService> logger) : IAirportTerminalService
{
    public async Task<CommonResponse<PagedResult<AirportTerminalItem>>> GetAirportTerminalsAsync(
        int? vendorId, PaginationRequest request, CancellationToken cancellationToken = default)
    {
        if (vendorId is null or < 1)
            return ResponseFactory.Failure<PagedResult<AirportTerminalItem>>(StatusCodes.Status400BadRequest, "A valid vendorId is required.", "INVALID_VENDOR_ID");
        if (request.CurrentPage < 1)
            return ResponseFactory.Failure<PagedResult<AirportTerminalItem>>(StatusCodes.Status400BadRequest, "CurrentPage must be greater than zero.", "INVALID_CURRENT_PAGE");
        if (request.PageSize is < 1 or > 100)
            return ResponseFactory.Failure<PagedResult<AirportTerminalItem>>(StatusCodes.Status400BadRequest, "PageSize must be between 1 and 100.", "INVALID_PAGE_SIZE");
        if (request.Keyword is { Length: > 100 })
            return ResponseFactory.Failure<PagedResult<AirportTerminalItem>>(StatusCodes.Status400BadRequest, "Keyword can be at most 100 characters.", "INVALID_KEYWORD");

        try
        {
            var terminals = await searchRepository.GetAirportTerminalsAsync(vendorId.Value, request, cancellationToken);
            return ResponseFactory.Success(terminals, terminals.TotalItems == 0
                ? "No airport terminals found."
                : "Airport terminals retrieved successfully.");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while fetching airport terminals for vendor {VendorId} (SQL error {Number}).", vendorId, ex.Number);
            return ResponseFactory.Failure<PagedResult<AirportTerminalItem>>(StatusCodes.Status500InternalServerError,
                "Unable to fetch airport terminals right now. Please try again.", "AIRPORT_TERMINALS_FETCH_FAILED");
        }
    }
}
