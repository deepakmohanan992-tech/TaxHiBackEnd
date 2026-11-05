using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchRepository searchRepository) : ControllerBase
{
    [HttpGet("locations")]
    public async Task<ActionResult<IReadOnlyList<SelectedItems>>> GetLocations(
        [FromQuery] string searchWord,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchWord))
        {
            return BadRequest("searchWord is required.");
        }

        var locations = await searchRepository.GetOutstationLocationsBySearchAsync(
            searchWord.Trim(),
            cancellationToken);

        return Ok(locations);
    }
}
