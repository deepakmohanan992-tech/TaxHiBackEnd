using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Maps;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// Google Places API (New) autocomplete / place details and Routes API computeRoutes.
/// The API key is sent only in the X-Goog-Api-Key header (never in a URL) and Google's raw error bodies are logged, not returned.
/// </summary>
public sealed partial class GoogleMapsService : IGoogleMapsService
{
    private const int MaxInputLength = 200;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,300}$")]
    private static partial Regex PlaceIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,36}$")]
    private static partial Regex SessionTokenPattern();

    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleMapsService> _logger;

    public GoogleMapsService(HttpClient client, IConfiguration configuration, ILogger<GoogleMapsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _client = client;
        _client.Timeout = TimeSpan.FromSeconds(
            int.TryParse(_configuration["GoogleMaps:HttpTimeoutSeconds"], out var t) && t > 0 ? t : 10);
    }

    // Read from appsettings.json / environment (GoogleMaps__ApiKey) on use.
    private string ApiKey => _configuration["GoogleMaps:ApiKey"] ?? string.Empty;
    private string PlacesBaseUrl => _configuration["GoogleMaps:PlacesBaseUrl"] is { Length: > 0 } p ? p : "https://places.googleapis.com/";
    private string RoutesBaseUrl => _configuration["GoogleMaps:RoutesBaseUrl"] is { Length: > 0 } r ? r : "https://routes.googleapis.com/";

    private bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    // ------------------------------------------------------------------ autocomplete

    public async Task<CommonResponse<IReadOnlyList<PlaceSuggestionDto>>> AutocompleteAsync(
        PlaceAutocompleteRequest request, CancellationToken cancellationToken = default)
    {
        var input = request.Input?.Trim();
        if (string.IsNullOrEmpty(input) || input.Length > MaxInputLength)
            return Fail<IReadOnlyList<PlaceSuggestionDto>>(StatusCodes.Status400BadRequest, $"Input is required (max {MaxInputLength} characters).", "INVALID_INPUT");
        if (!IsValidSessionToken(request.SessionToken))
            return Fail<IReadOnlyList<PlaceSuggestionDto>>(StatusCodes.Status400BadRequest, "SessionToken is not valid.", "INVALID_SESSION_TOKEN");
        if (!IsConfigured)
            return NotConfigured<IReadOnlyList<PlaceSuggestionDto>>();

        var body = new Dictionary<string, object?>
        {
            ["input"] = input,
            ["includedRegionCodes"] = new[] { "in" },
            ["sessionToken"] = string.IsNullOrWhiteSpace(request.SessionToken) ? null : request.SessionToken
        };

        using var message = BuildRequest(HttpMethod.Post, Url(PlacesBaseUrl, "v1/places:autocomplete"),
            "suggestions.placePrediction.placeId,suggestions.placePrediction.text.text");
        message.Content = JsonContent.Create(body.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

        var (json, error) = await SendAsync<IReadOnlyList<PlaceSuggestionDto>>(message, "autocomplete", cancellationToken);
        if (error is not null) return error;

        var suggestions = new List<PlaceSuggestionDto>();
        if (json!.Value.TryGetProperty("suggestions", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("placePrediction", out var prediction)) continue;
                var placeId = prediction.TryGetProperty("placeId", out var id) ? id.GetString() : null;
                var text = prediction.TryGetProperty("text", out var t) && t.TryGetProperty("text", out var tt) ? tt.GetString() : null;
                if (!string.IsNullOrEmpty(placeId) && !string.IsNullOrEmpty(text))
                    suggestions.Add(new PlaceSuggestionDto { PlaceId = placeId, FormattedAddress = text });
            }
        }

        return ResponseFactory.Success<IReadOnlyList<PlaceSuggestionDto>>(suggestions,
            suggestions.Count == 0 ? "No locations found." : "Locations retrieved successfully.");
    }

    // ------------------------------------------------------------------ place details

    public async Task<CommonResponse<PlaceDetailsDto>> GetPlaceDetailsAsync(
        string placeId, string? sessionToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(placeId) || !PlaceIdPattern().IsMatch(placeId))
            return Fail<PlaceDetailsDto>(StatusCodes.Status400BadRequest, "A valid PlaceId is required.", "INVALID_PLACE_ID");
        if (!IsValidSessionToken(sessionToken))
            return Fail<PlaceDetailsDto>(StatusCodes.Status400BadRequest, "SessionToken is not valid.", "INVALID_SESSION_TOKEN");
        if (!IsConfigured)
            return NotConfigured<PlaceDetailsDto>();

        var url = Url(PlacesBaseUrl, $"v1/places/{placeId}");
        if (!string.IsNullOrWhiteSpace(sessionToken)) url += "?sessionToken=" + Uri.EscapeDataString(sessionToken);

        using var message = BuildRequest(HttpMethod.Get, url, "id,formattedAddress,location");
        var (json, error) = await SendAsync<PlaceDetailsDto>(message, "place details", cancellationToken);
        if (error is not null) return error;

        var root = json!.Value;
        if (!root.TryGetProperty("location", out var location)
            || !location.TryGetProperty("latitude", out var lat) || !location.TryGetProperty("longitude", out var lng))
            return Fail<PlaceDetailsDto>(StatusCodes.Status502BadGateway, "The location service returned an unexpected response.", "MAPS_BAD_RESPONSE");

        return ResponseFactory.Success(new PlaceDetailsDto
        {
            PlaceId = root.TryGetProperty("id", out var id) ? id.GetString() ?? placeId : placeId,
            FormattedAddress = root.TryGetProperty("formattedAddress", out var addr) ? addr.GetString() ?? string.Empty : string.Empty,
            Latitude = lat.GetDouble(),
            Longitude = lng.GetDouble()
        }, "Place details retrieved successfully.");
    }

    // ------------------------------------------------------------------ routes

    public async Task<CommonResponse<RouteResultDto>> ComputeRouteAsync(
        ComputeRouteRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsValidCoordinate(request.PickupLatitude, request.PickupLongitude))
            return Fail<RouteResultDto>(StatusCodes.Status400BadRequest, "Pickup latitude/longitude are not valid.", "INVALID_PICKUP_COORDINATES");
        if (!IsValidCoordinate(request.DropLatitude, request.DropLongitude))
            return Fail<RouteResultDto>(StatusCodes.Status400BadRequest, "Drop latitude/longitude are not valid.", "INVALID_DROP_COORDINATES");
        if (!IsConfigured)
            return NotConfigured<RouteResultDto>();

        var body = new
        {
            origin = new { location = new { latLng = new { latitude = request.PickupLatitude, longitude = request.PickupLongitude } } },
            destination = new { location = new { latLng = new { latitude = request.DropLatitude, longitude = request.DropLongitude } } },
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_UNAWARE",
            units = "METRIC"
        };

        using var message = BuildRequest(HttpMethod.Post, Url(RoutesBaseUrl, "directions/v2:computeRoutes"),
            "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline");
        message.Content = JsonContent.Create(body);

        var (json, error) = await SendAsync<RouteResultDto>(message, "compute routes", cancellationToken);
        if (error is not null) return error;

        if (!json!.Value.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
            return Fail<RouteResultDto>(StatusCodes.Status404NotFound, "No drivable route was found between the selected locations.", "ROUTE_NOT_FOUND");

        var route = routes[0];
        var meters = route.TryGetProperty("distanceMeters", out var d) ? d.GetInt64() : 0;
        var seconds = route.TryGetProperty("duration", out var du) ? ParseSeconds(du.GetString()) : 0;
        var polyline = route.TryGetProperty("polyline", out var p) && p.TryGetProperty("encodedPolyline", out var ep) ? ep.GetString() : null;

        if (string.IsNullOrEmpty(polyline))
            return Fail<RouteResultDto>(StatusCodes.Status502BadGateway, "The routing service returned an unexpected response.", "MAPS_BAD_RESPONSE");

        //Fare calculation part
        decimal baseFare;
        decimal extraKilometres = 0m;
        decimal extraCharge = 0m;
        decimal googleDistanceKm = Math.Round(meters / 1000m, 2, MidpointRounding.AwayFromZero);

        if (googleDistanceKm < Convert.ToDecimal(request.IncludedKilometres))
        {
            // Calculate proportionate fare using OriginalAmount.
            baseFare =
                (Convert.ToDecimal(request.OriginalAmount) / Convert.ToDecimal(request.IncludedKilometres))
                * googleDistanceKm;
        }
        else
        {
            // Use OriginalAmount as the base package fare.
            baseFare = Convert.ToDecimal(request.OriginalAmount);

            // Calculate extra kilometres.
            extraKilometres =
                googleDistanceKm - Convert.ToDecimal(request.IncludedKilometres);

            // Calculate extra kilometre charge.
            extraCharge =
                extraKilometres * Convert.ToDecimal(request.PostLimitChargePerKM);

            // Add extra charge to base fare.
            baseFare += extraCharge;
        }

        // GST calculation on total fare.
        var gst = baseFare * Convert.ToDecimal(request.GstPercentage) / 100m;

        // Full payment includes base fare and GST.
        var fullPay = baseFare + gst;

        // Part payment is 25% of FullPay.
        var partPay = fullPay * 0.25m;

        return ResponseFactory.Success(new RouteResultDto
        {
            GoogleDistanceKm = googleDistanceKm,
            GoogleDurationMinutes = (int)Math.Ceiling(seconds / 60d),
            BaseFare = baseFare,
            GST = gst,
            FullPay = fullPay,
            PartPay = partPay,
            EncodedPolyline = polyline
        }, "Route calculated successfully.");
    }

    // ------------------------------------------------------------------ helpers

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string fieldMask)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.Add("X-Goog-Api-Key", ApiKey);
        message.Headers.Add("X-Goog-FieldMask", fieldMask);
        return message;
    }

    /// <summary>Sends the request; returns either the parsed JSON body or a ready-made failure response.</summary>
    private async Task<(JsonElement? Json, CommonResponse<T>? Error)> SendAsync<T>(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google {Operation} failed with HTTP {Status}: {Body}", operation, (int)response.StatusCode, Truncate(content));
                return (null, response.StatusCode switch
                {
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
                        Fail<T>(StatusCodes.Status400BadRequest, "The request was rejected by the location service. Check the input and try again.", "MAPS_INVALID_REQUEST"),
                    HttpStatusCode.TooManyRequests =>
                        Fail<T>(StatusCodes.Status503ServiceUnavailable, "The location service is busy. Please try again shortly.", "MAPS_RATE_LIMITED"),
                    _ => Fail<T>(StatusCodes.Status502BadGateway, "The location service is unavailable right now. Please try again.", "MAPS_UPSTREAM_ERROR")
                });
            }

            using var doc = JsonDocument.Parse(content);
            return (doc.RootElement.Clone(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex) // HttpClient timeout
        {
            _logger.LogWarning(ex, "Google {Operation} timed out.", operation);
            return (null, Fail<T>(StatusCodes.Status504GatewayTimeout, "The location service timed out. Please try again.", "MAPS_TIMEOUT"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "Google {Operation} call failed.", operation);
            return (null, Fail<T>(StatusCodes.Status502BadGateway, "The location service is unavailable right now. Please try again.", "MAPS_UPSTREAM_ERROR"));
        }
    }

    private CommonResponse<T> NotConfigured<T>()
    {
        _logger.LogError("GoogleMaps:ApiKey is not configured.");
        return Fail<T>(StatusCodes.Status503ServiceUnavailable, "Location services are not available right now.", "MAPS_NOT_CONFIGURED");
    }

    private static CommonResponse<T> Fail<T>(int status, string message, string code) => ResponseFactory.Failure<T>(status, message, code);

    private static string Url(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path;

    private static bool IsValidSessionToken(string? token) => string.IsNullOrWhiteSpace(token) || SessionTokenPattern().IsMatch(token);

    private static bool IsValidCoordinate(double lat, double lng) =>
        double.IsFinite(lat) && double.IsFinite(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);

    private static double ParseSeconds(string? duration) =>
        duration is { Length: > 1 } && duration[^1] == 's'
        && double.TryParse(duration[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
