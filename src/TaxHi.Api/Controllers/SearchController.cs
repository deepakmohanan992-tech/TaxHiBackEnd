using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(
    ISearchRepository searchRepository,
    IAirportTerminalService airportTerminalService,
    IRouteDistanceService routeDistanceService,
    IUserContext userContext) : ControllerBase
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

    // Active airport terminals under the vendor's sector (popular first). The vendor comes from the vendorId
    // query parameter, read through IUserContext exactly like the lookup endpoints; paging/search from PaginationRequest.
    [HttpGet("airportTerminals")]
    public async Task<ActionResult<CommonResponse<PagedResult<AirportTerminalItem>>>> GetAirportTerminals(
        [FromQuery] PaginationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await airportTerminalService.GetAirportTerminalsAsync(26, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
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

        // A coordinate pair must be complete; both pairs are optional (km is then 0).
        if (request.PickupLatitude.HasValue != request.PickupLongitude.HasValue)
        {
            return BadRequest(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PickupLatitude and PickupLongitude must be provided together.",
                ErrorCode = "INVALID_PICKUP_COORDINATES"
            });
        }

        if (request.DropLatitude.HasValue != request.DropLongitude.HasValue)
        {
            return BadRequest(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "DropLatitude and DropLongitude must be provided together.",
                ErrorCode = "INVALID_DROP_COORDINATES"
            });
        }

        // The procedure needs a pickup and a drop, each identified by a place id or by coordinates.
        if (string.IsNullOrWhiteSpace(request.PickupPlaceId) && !request.PickupLatitude.HasValue
            || string.IsNullOrWhiteSpace(request.DropPlaceId) && !request.DropLatitude.HasValue)
        {
            return BadRequest(new CommonResponse<PagedResult<AvailableCabListing>>
            {
                Success = false,
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "Pickup and drop locations are required.",
                ErrorCode = "LOCATIONS_REQUIRED"
            });
        }

        var km = await routeDistanceService.CalculateKmAsync(request, cancellationToken);
        var result = await searchRepository.GetAvailableCabListingAsync(request, km, cancellationToken);

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
