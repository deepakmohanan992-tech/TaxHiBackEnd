namespace TaxHi.Application.Models.Maps;

public sealed class PlaceAutocompleteRequest
{
    public string Input { get; init; } = string.Empty;
    /// <summary>Optional client-generated token grouping the autocomplete keystrokes and the final place-details call into one billed session.</summary>
    public string? SessionToken { get; init; }
}

public sealed class PlaceSuggestionDto
{
    public string PlaceId { get; set; } = string.Empty;
    public string FormattedAddress { get; set; } = string.Empty;
}

public sealed class PlaceDetailsDto
{
    public string PlaceId { get; set; } = string.Empty;
    public string FormattedAddress { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class ComputeRouteRequest
{
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public double DropLatitude { get; init; }
    public double DropLongitude { get; init; }
    public double OriginalAmount { get; init; }
    public double GstPercentage { get; init; }
    public double IncludedKilometres { get; init; }
    public double PostLimitChargePerKM { get; init; }
}

public sealed class RouteResultDto
{
    /// <summary>Google-calculated road distance. Informational only; never used for billing.</summary>
    public decimal GoogleDistanceKm { get; set; }
    public int GoogleDurationMinutes { get; set; }
    //public string EncodedPolyline { get; set; } = string.Empty;
    public decimal PartPay { get; init; }
    public decimal FullPay { get; init; }
    public decimal BaseFare { get; init; }
    public decimal GST { get; init; }
}

/// <summary>Optional route details sent with a booking and stored apart from the billing distance.</summary>
public sealed class BookingRouteDetailsDto
{
    public string? PickupPlaceId { get; init; }
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public string? DropPlaceId { get; init; }
    public double DropLatitude { get; init; }
    public double DropLongitude { get; init; }
    public decimal GoogleDistanceKm { get; init; }
    public int GoogleDurationMinutes { get; init; }
    public string? EncodedPolyline { get; init; }
}
