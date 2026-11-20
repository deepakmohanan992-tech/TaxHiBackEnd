using TaxHi.Application.Models;
using TaxHi.Application.Models.Maps;

namespace TaxHi.Application.Services;

public interface IGoogleMapsService
{
    Task<CommonResponse<IReadOnlyList<PlaceSuggestionDto>>> AutocompleteAsync(PlaceAutocompleteRequest request, CancellationToken cancellationToken = default);

    Task<CommonResponse<PlaceDetailsDto>> GetPlaceDetailsAsync(string placeId, string? sessionToken, CancellationToken cancellationToken = default);

    Task<CommonResponse<RouteResultDto>> ComputeRouteAsync(ComputeRouteRequest request, CancellationToken cancellationToken = default);
}
