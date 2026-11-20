using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Maps;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

// Anonymous like api/search (customers pick locations before signing in); rate-limited to protect the Google quota.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("maps")]
[Route("api/Maps")]
public sealed class MapsController(IGoogleMapsService mapsService) : ControllerBase
{
    [HttpPost("Autocomplete")]
    public async Task<ActionResult<CommonResponse<IReadOnlyList<PlaceSuggestionDto>>>> Autocomplete(
        [FromBody] PlaceAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mapsService.AutocompleteAsync(request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("PlaceDetails/{placeId}")]
    public async Task<ActionResult<CommonResponse<PlaceDetailsDto>>> PlaceDetails(
        string placeId,
        [FromQuery] string? sessionToken,
        CancellationToken cancellationToken)
    {
        var response = await mapsService.GetPlaceDetailsAsync(placeId, sessionToken, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("ComputeRoute")]
    public async Task<ActionResult<CommonResponse<RouteResultDto>>> ComputeRoute(
        [FromBody] ComputeRouteRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mapsService.ComputeRouteAsync(request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
