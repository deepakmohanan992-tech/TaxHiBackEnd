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
                Data = Array.Empty<SelectedItems>()
            });
        }

        var locations = await searchRepository.GetOutstationLocationsBySearchAsync(
            searchWord.Trim(),
            cancellationToken);

        return Ok(new CommonResponse<IReadOnlyList<SelectedItems>>
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Message = "Locations retrieved successfully.",
            Data = locations
        });
    }
}
