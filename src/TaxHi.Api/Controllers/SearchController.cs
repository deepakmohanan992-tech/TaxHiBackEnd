using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchRepository searchRepository) : ControllerBase
{
    [HttpGet("locations")]
    public async Task<ActionResult<CommonResponse<IReadOnlyList<SelectedItems>>>> GetLocations(
        [FromQuery] string searchWord,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchWord))
        {
            return BadRequest(new CommonResponse<IReadOnlyList<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "searchWord is required.",
                ErrorCode = "SEARCH_WORD_REQUIRED",
                Data = []
            });
        }

        var locations = await searchRepository.GetOutstationLocationsBySearchAsync(
            searchWord.Trim(),
            cancellationToken);

        if (locations is null || locations.Count == 0)
        {
            return NotFound(new CommonResponse<IReadOnlyList<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status404NotFound,
                Message = "No locations found.",
                ErrorCode = "LOCATIONS_NOT_FOUND",
                Data = []
            });
        }

        return Ok(new CommonResponse<IReadOnlyList<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Locations retrieved successfully.",
            Data = locations
        });
    }

    [HttpPost("availableCabListing")]
    public async Task<ActionResult<CommonResponse<IReadOnlyList<AvailableCabListing>>>> GetAvailableCabListing(
        [FromQuery] int? vendorId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int? locationFrom,
        [FromQuery] int? locationTo,
        CancellationToken cancellationToken)
    {
        var listings = await searchRepository.GetAvailableCabListingAsync(
            vendorId,
                fromDate,
                toDate,
            locationFrom,
            locationTo,
            cancellationToken);

            if (listings is null || listings.Count == 0)
            {
                return NotFound(new CommonResponse<IReadOnlyList<AvailableCabListing>>
                {
                    Success = false,
                    StatusCode = StatusCodes.Status404NotFound,
                    Message = "No available cab listings found.",
                    ErrorCode = "AVAILABLE_CAB_LISTINGS_NOT_FOUND",
                    Data = []
                });
            }

        return Ok(new CommonResponse<IReadOnlyList<AvailableCabListing>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Available cab listings retrieved successfully.",
            Data = listings
        });
    }
}
