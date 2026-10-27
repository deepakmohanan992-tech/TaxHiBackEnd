using Microsoft.AspNetCore.Mvc;
using TaxHi.Application.Features.FareQuotes;
using TaxHi.Application.Features.FareQuotes.Dtos;

namespace TaxHi.Controllers;

[ApiController]
[Route("api/fare-quotes")]
public sealed class FareQuotesController : ControllerBase
{
    private readonly FareQuoteService _service;

    public FareQuotesController(FareQuoteService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<FareQuoteResponse>> Create([FromBody] FareQuoteRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { quoteId = result.QuoteId }, result);
    }

    [HttpGet("{quoteId:guid}")]
    public async Task<ActionResult<FareQuoteResponse>> Get(Guid quoteId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(quoteId, cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }
}