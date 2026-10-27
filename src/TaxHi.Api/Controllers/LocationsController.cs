using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Locations.Dtos;

namespace TaxHi.Controllers;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationsController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpGet("search")]
    public async Task<ActionResult<LocationSearchResponse>> Search([FromQuery] string query, CancellationToken cancellationToken)
    {
        var locations = await _locationService.SearchAsync(query, cancellationToken);
        var items = locations.Select(l => new LocationDto(l.Id, l.Name, l.City, l.Type.ToString(), l.Latitude, l.Longitude)).ToList();
        return Ok(new LocationSearchResponse(items, true, "Development sample data"));
    }
}