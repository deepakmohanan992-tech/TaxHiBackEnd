using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Authentication;
using TaxHi.Application.Services;

namespace TaxHi.Api.Controllers;

[ApiController]
[Route("api/Authentication")]
public sealed class AuthenticationController(IAuthenticationService authenticationService) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [HttpPost("sendOTP")]
    public Task<ActionResult<CommonResponse<object>>> SendOtp([FromBody] SendOtpRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.SendOtpAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("verifyOTP")]
    public Task<ActionResult<CommonResponse<TokenResponseDto>>> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.VerifyOtpAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("refreshToken")]
    public Task<ActionResult<CommonResponse<TokenResponseDto>>> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.RefreshTokenAsync(request, cancellationToken));

    [Authorize]
    [HttpPost("revokeToken")]
    public Task<ActionResult<CommonResponse<object>>> RevokeToken([FromBody] RevokeTokenRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.RevokeTokenAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("/api/auth/logout")]
    public Task<ActionResult<CommonResponse<object>>> Logout([FromBody] RevokeTokenRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.LogoutAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpGet("/api/auth/context")]
    public Task<ActionResult<CommonResponse<SessionContextResponse>>> GetSessionContext(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userId = long.TryParse(subject, out var parsedUserId) ? parsedUserId : (long?)null;

        return Execute(authenticationService.GetSessionContextAsync(userId, cancellationToken));
    }

    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [HttpPost("sendGuestCancellationOTP")]
    public Task<ActionResult<CommonResponse<object>>> SendGuestCancellationOtp([FromBody] GuestCancellationOtpRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.SendGuestCancellationOtpAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("verifyGuestCancellationOTP")]
    public Task<ActionResult<CommonResponse<object>>> VerifyGuestCancellationOtp([FromBody] VerifyGuestCancellationOtpRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.VerifyGuestCancellationOtpAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("validateGuestBookingAccess")]
    public Task<ActionResult<CommonResponse<GuestBookingAccessDto>>> ValidateGuestBookingAccess([FromBody] ValidateGuestBookingAccessRequest request, CancellationToken cancellationToken) =>
        Execute(authenticationService.ValidateGuestBookingAccessAsync(request, cancellationToken));

    private async Task<ActionResult<CommonResponse<T>>> Execute<T>(Task<CommonResponse<T>> operation)
    {
        var response = await operation;
        return new ObjectResult(response) { StatusCode = response.StatusCode };
    }
}
