using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Vehicles.Dtos;
using TaxHi.Domain.Enums;

namespace TaxHi.Controllers;

[ApiController]
[Route("api/vehicles")]
public sealed class VehiclesController : ControllerBase
{
    private readonly IVehicleCatalog _catalog;
    private readonly IVehicleAvailabilityService _availability;

    public VehiclesController(IVehicleCatalog catalog, IVehicleAvailabilityService availability)
    {
        _catalog = catalog;
        _availability = availability;
    }

    [HttpGet("categories")]
    public ActionResult<IReadOnlyList<VehicleCategoryDto>> Categories()
    {
        var items = _catalog.GetAll().Select(v => new VehicleCategoryDto(v.Code, v.DisplayName, v.PassengerCapacity, v.LuggageCapacity, v.Features)).ToList();
        return Ok(items);
    }

    [HttpGet("availability")]
    public async Task<ActionResult<VehicleAvailabilityResponse>> Availability(
        [FromQuery] string pickup,
        [FromQuery] string drop,
        [FromQuery] DateOnly date,
        [FromQuery] TripType tripType = TripType.OneWay,
        CancellationToken cancellationToken = default)
    {
        var items = await _availability.GetAvailabilityAsync(pickup, drop, date, tripType, cancellationToken);
        var dtoItems = items.Select(i => new VehicleAvailabilityItemDto(i.CategoryCode, i.DisplayName, i.PassengerCapacity, i.LuggageCapacity, i.Features, i.Available, i.Reason, i.IsSimulated)).ToList();
        return Ok(new VehicleAvailabilityResponse(pickup, drop, date, tripType, dtoItems, true, "Simulated availability based on development sample data."));
    }
}