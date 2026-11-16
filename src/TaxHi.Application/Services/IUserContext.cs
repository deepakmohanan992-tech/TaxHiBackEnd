namespace TaxHi.Application.Services;

/// <summary>
/// Describes the caller of the current request. Every value is null when it is absent
/// or malformed (anonymous request, missing claim, no active HTTP request), so reading it never throws.
/// </summary>
public interface IUserContext
{
    /// <summary>True when the request carries a valid access token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>User id from the token's subject claim.</summary>
    long? UserId { get; }

    /// <summary>Email claim from the token.</summary>
    string? Email { get; }

    /// <summary>Name claim from the token.</summary>
    string? Name { get; }

    /// <summary>Mobile number claim from the token.</summary>
    string? PhoneNumber { get; }

    /// <summary>
    /// Vendor id from the <c>vendorId</c> query string parameter. This is supplied by the client,
    /// so it identifies which vendor is being asked about, not which vendor the caller is allowed to act for.
    /// </summary>
    int? VendorId { get; }
}
