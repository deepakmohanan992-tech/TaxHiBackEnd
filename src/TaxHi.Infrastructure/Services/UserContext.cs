using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Services;

public sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private const string VendorIdQueryKey = "vendorId";

    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    private ClaimsPrincipal? User => HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    // The JWT bearer handler may or may not remap the short JWT claim names to the
    // long ClaimTypes URIs depending on configuration, so both forms are checked.
    public long? UserId =>
        long.TryParse(FindClaim(ClaimTypes.NameIdentifier, JwtRegisteredClaimNames.Sub), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;

    public string? Email => FindClaim(ClaimTypes.Email, JwtRegisteredClaimNames.Email);

    public string? Name => FindClaim(ClaimTypes.Name, JwtRegisteredClaimNames.Name);

    public string? PhoneNumber => FindClaim(ClaimTypes.MobilePhone, JwtRegisteredClaimNames.PhoneNumber);

    public int? VendorId
    {
        get
        {
            var headers = HttpContext?.Request.Headers;
            if (headers is null || !headers.TryGetValue("vendorId", out var values))
                return null;

            return int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id
                : null;
        }
    }

    private string? FindClaim(params string[] claimTypes)
    {
        var user = User;
        if (user is null)
            return null;

        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
