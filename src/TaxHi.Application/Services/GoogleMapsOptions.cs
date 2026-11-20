namespace TaxHi.Application.Services;

/// <summary>Google Maps Platform settings. ApiKey must come from user-secrets / environment (GoogleMaps__ApiKey), never source control.</summary>
public sealed class GoogleMapsOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string PlacesBaseUrl { get; set; } = "https://places.googleapis.com/";
    public string RoutesBaseUrl { get; set; } = "https://routes.googleapis.com/";
    public int HttpTimeoutSeconds { get; set; } = 10;
}
