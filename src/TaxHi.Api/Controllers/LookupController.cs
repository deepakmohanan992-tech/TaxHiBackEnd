using System.ComponentModel;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Enums;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/lookup")]
public sealed class LookupController(ILookupRepository lookupRepository) : ControllerBase
{
    [HttpGet("vehicleTypes")]
    public async Task<ActionResult<CommonResponse<IReadOnlyList<SelectedItems>>>> GetVehicleTypes(
        CancellationToken cancellationToken)
    {
        var vehicleTypes = await lookupRepository.GetVehicleTypesAsync(cancellationToken);

        if (vehicleTypes.Count == 0)
        {
            return NotFound(new CommonResponse<IReadOnlyList<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status200OK,
                Message = "No vehicle types found.",
                ErrorCode = "VEHICLE_TYPES_NOT_FOUND",
                Data = vehicleTypes
            });
        }

        return Ok(new CommonResponse<IReadOnlyList<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Vehicle types retrieved successfully.",
            Data = vehicleTypes
        });
    }

    [HttpGet("seatingCapacity")]
    public async Task<ActionResult<CommonResponse<IReadOnlyList<SelectedItems>>>> GetSeatingCapacity(
        CancellationToken cancellationToken)
    {
        var seatingCapacity = await lookupRepository.GetSeatingCapacityAsync(cancellationToken);

        if (seatingCapacity.Count == 0)
        {
            return NotFound(new CommonResponse<IReadOnlyList<SelectedItems>>
            {
                Success = false,
                StatusCode = StatusCodes.Status200OK,
                Message = "No seating capacity values found.",
                ErrorCode = "SEATING_CAPACITY_NOT_FOUND",
                Data = seatingCapacity
            });
        }

        return Ok(new CommonResponse<IReadOnlyList<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Seating capacity values retrieved successfully.",
            Data = seatingCapacity
        });
    }

    [HttpGet("recommended")]
    public ActionResult<CommonResponse<IReadOnlyList<SelectedItems>>> GetRecommended()
    {
        var recommended = Enum.GetValues<Recommended>()
            .Select(option => new SelectedItems
            {
                Id = (int)option,
                Value = typeof(Recommended)
                    .GetMember(option.ToString())[0]
                    .GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? option.ToString()
            })
            .ToList();

        return Ok(new CommonResponse<IReadOnlyList<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Recommended options retrieved successfully.",
            Data = recommended
        });
    }
}
