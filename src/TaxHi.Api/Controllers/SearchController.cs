using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchRepository searchRepository) : ControllerBase
{
    [HttpPost("locations")]
    public async Task<ActionResult<CommonResponse<PagedResult<SelectedItems>>>> GetLocations(
        [FromBody] LocationSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
        {
            return BadRequest(new CommonResponse<PagedResult<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Keyword is required.",
                ErrorCode = "KEYWORD_REQUIRED"
            });
        }

        if (request.CurrentPage < 1)
        {
            return BadRequest(new CommonResponse<PagedResult<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "CurrentPage must be greater than zero.",
                ErrorCode = "INVALID_CURRENT_PAGE"
            });
        }

        if (request.PageSize is < 1 or > 100)
        {
            return BadRequest(new CommonResponse<PagedResult<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PageSize must be between 1 and 100.",
                ErrorCode = "INVALID_PAGE_SIZE"
            });
        }

        var locations = await searchRepository.GetOutstationLocationsBySearchAsync(
            request,
            cancellationToken);

        if (locations.TotalItems == 0 || locations.Items.Count == 0)
        {
            return NotFound(new CommonResponse<PagedResult<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status200OK,
                Message = "No locations found.",
                ErrorCode = "LOCATIONS_NOT_FOUND",
                Data = locations
            });
        }

        return Ok(new CommonResponse<PagedResult<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Locations retrieved successfully.",
            Data = locations
        });
    }

    [HttpPost("availableCabListing")]
    public async Task<ActionResult<CommonResponse<PagedResult<AvailableCabListing>>>> GetAvailableCabListing(
        [FromBody] AvailableCabListingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.CurrentPage < 1)
        {
            return BadRequest(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "CurrentPage must be greater than zero.",
                ErrorCode = "INVALID_CURRENT_PAGE"
            });
        }

        if (request.PageSize is < 1 or > 100)
        {
            return BadRequest(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PageSize must be between 1 and 100.",
                ErrorCode = "INVALID_PAGE_SIZE"
            });
        }

        var result = await searchRepository.GetAvailableCabListingAsync(request, cancellationToken);

        if (result.TotalItems == 0 || result.Items.Count == 0)
        {
            return NotFound(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status200OK,
                Message = "No vehicles available for the selected locations and dates.",
                ErrorCode = "AVAILABLE_CAB_LISTINGS_NOT_FOUND",
                Data = result
            });
        }

        return Ok(new CommonResponse<PagedResult<AvailableCabListing>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Available cab listings retrieved successfully.",
            Data = result
        });
    }
}
