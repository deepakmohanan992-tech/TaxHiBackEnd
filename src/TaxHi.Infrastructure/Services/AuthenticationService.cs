using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Authentication;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;
using TaxHi.Infrastructure.Helpers;

namespace TaxHi.Infrastructure.Services;

public sealed class AuthenticationService(
    IAuthenticationRepository repository,
    IOtpNotifier otpNotifier,
    IOptions<AuthenticationOptions> options) : IAuthenticationService
{
    private readonly AuthenticationOptions settings = options.Value;

    public async Task<CommonResponse<object>> SendOtpAsync(SendOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (!MobileNumberHelper.IsValidMobile(request.MobileNumber) || !string.Equals(request.Purpose?.Trim(), "Login", StringComparison.OrdinalIgnoreCase))
            return ResponseFactory.Failure<object>(400, "A valid mobile number and purpose are required.", "INVALID_OTP_REQUEST");

        var otp = OtpService.CreateNumericCode(6);
        var result = await repository.CreateOtpAsync(
            MobileNumberHelper.NormalizeMobile(request.MobileNumber), "Login", TokenHelper.Hash(otp, settings.JwtSigningKey),
            DateTime.UtcNow.AddMinutes(settings.OtpExpiryMinutes), settings.OtpMaxAttempts,
            settings.OtpResendCooldownSeconds, cancellationToken);

        if (!result.Accepted)
            return ResponseFactory.Failure<object>(429, "OTP resend is temporarily unavailable.", "OTP_RATE_LIMITED", result.RetryAfterSeconds);

        await otpNotifier.SendAsync(MobileNumberHelper.NormalizeMobile(request.MobileNumber), "Login", otp, cancellationToken);
        return ResponseFactory.Success<object>(new { ExpiresUtc = result.ExpiresUtc }, "OTP sent successfully.");
    }

    public async Task<CommonResponse<TokenResponseDto>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (!MobileNumberHelper.IsValidMobile(request.MobileNumber) || !string.Equals(request.Purpose?.Trim(), "Login", StringComparison.OrdinalIgnoreCase) || request.Otp.Length != 6 || !request.Otp.All(char.IsDigit))
            return ResponseFactory.Failure<TokenResponseDto>(400, "A valid mobile number and OTP are required.", "INVALID_OTP_REQUEST");

        var verification = await repository.VerifyOtpAsync(MobileNumberHelper.NormalizeMobile(request.MobileNumber), "Login", TokenHelper.Hash(request.Otp, settings.JwtSigningKey), cancellationToken);
        if (!verification.Success)
            return ResponseFactory.Failure<TokenResponseDto>(401, "The OTP is invalid, expired, or has exceeded the maximum attempts.", verification.FailureCode);

        var user = await repository.RegisterOrGetUserAsync(MobileNumberHelper.NormalizeMobile(request.MobileNumber), cancellationToken);
        return ResponseFactory.Success(await IssueTokensAsync(user, cancellationToken), "Authentication successful.");
    }

    public async Task<CommonResponse<SessionContextResponse>> GetSessionContextAsync(long? userId, CancellationToken cancellationToken = default)
    {
        if (userId is null or <= 0)
            return ResponseFactory.Success(new SessionContextResponse(), "Session context retrieved successfully.");

        var user = await repository.GetUserByIdAsync(userId.Value, cancellationToken);
        if (user is null)
            return ResponseFactory.Success(new SessionContextResponse(), "Session context retrieved successfully.");

        return ResponseFactory.Success(new SessionContextResponse
        {
            UserType = "Registered",
            UserId = user.UserId,
            MobileNumber = user.MobileNumber,
            IsGuest = false
        }, "Session context retrieved successfully.");
    }

    public async Task<CommonResponse<TokenResponseDto>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ResponseFactory.Failure<TokenResponseDto>(400, "Refresh token is required.", "REFRESH_TOKEN_REQUIRED");

        var tokenHash = TokenHelper.Hash(request.RefreshToken, settings.JwtSigningKey);
        var user = await repository.ValidateRefreshTokenAsync(tokenHash, cancellationToken);
        if (user is null)
            return ResponseFactory.Failure<TokenResponseDto>(401, "Refresh token is invalid or expired.", "INVALID_REFRESH_TOKEN");

        await repository.RevokeRefreshTokenAsync(tokenHash, cancellationToken);
        return ResponseFactory.Success(await IssueTokensAsync(new UserInfoDto
        {
            UserId = user.UserId, MobileNumber = user.MobileNumber, Name = user.Name, Email = user.Email
        }, cancellationToken), "Token refreshed successfully.");
    }

    public async Task<CommonResponse<object>> LogoutAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ResponseFactory.Failure<object>(400, "Refresh token is required.", "REFRESH_TOKEN_REQUIRED");

        var tokenHash = TokenHelper.Hash(request.RefreshToken, settings.JwtSigningKey);
        var status = await repository.GetRefreshTokenStatusAsync(tokenHash, cancellationToken);
        if (status is null)
            return ResponseFactory.Failure<object>(401, "Refresh token was not found.", "INVALID_REFRESH_TOKEN");
        if (status.IsRevoked)
            return ResponseFactory.Failure<object>(401, "Refresh token has already been revoked.", "REFRESH_TOKEN_REVOKED");
        if (status.IsExpired)
            return ResponseFactory.Failure<object>(401, "Refresh token has expired.", "REFRESH_TOKEN_EXPIRED");

        await repository.RevokeRefreshTokenAsync(tokenHash, cancellationToken);
        return ResponseFactory.Success<object>(null, "Logged out successfully.");
    }

    public async Task<CommonResponse<object>> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return ResponseFactory.Failure<object>(400, "Refresh token is required.", "REFRESH_TOKEN_REQUIRED");

        await repository.RevokeRefreshTokenAsync(TokenHelper.Hash(request.RefreshToken, settings.JwtSigningKey), cancellationToken);
        return ResponseFactory.Success<object>(null, "Token revoked successfully.");
    }

    public async Task<CommonResponse<object>> SendGuestCancellationOtpAsync(GuestCancellationOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BookingReference) || !MobileNumberHelper.IsValidMobile(request.MobileNumber))
            return ResponseFactory.Failure<object>(400, "Booking reference and a valid mobile number are required.", "INVALID_GUEST_REQUEST");

        var booking = await repository.GetGuestBookingAsync(request.BookingReference.Trim(), MobileNumberHelper.NormalizeMobile(request.MobileNumber), cancellationToken);
        if (booking is null)
            return ResponseFactory.Failure<object>(404, "Booking was not found.", "BOOKING_NOT_FOUND");
        if (booking.Status is "Cancelled" or "Completed")
            return ResponseFactory.Failure<object>(409, "This booking cannot be cancelled.", "BOOKING_NOT_CANCELLABLE");

        var otp = OtpService.CreateNumericCode(6);
        var purpose = AuthConstants.GuestPurpose(request.BookingReference);
        var result = await repository.CreateOtpAsync(MobileNumberHelper.NormalizeMobile(request.MobileNumber), purpose, TokenHelper.Hash(otp, settings.JwtSigningKey),
            DateTime.UtcNow.AddMinutes(settings.OtpExpiryMinutes), settings.OtpMaxAttempts,
            settings.OtpResendCooldownSeconds, cancellationToken);
        if (!result.Accepted)
            return ResponseFactory.Failure<object>(429, "OTP resend is temporarily unavailable.", "OTP_RATE_LIMITED", result.RetryAfterSeconds);

        await otpNotifier.SendAsync(MobileNumberHelper.NormalizeMobile(request.MobileNumber), purpose, otp, cancellationToken);
        return ResponseFactory.Success<object>(new { ExpiresUtc = result.ExpiresUtc }, "Cancellation OTP sent successfully.");
    }

    public async Task<CommonResponse<object>> VerifyGuestCancellationOtpAsync(VerifyGuestCancellationOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BookingReference) || !MobileNumberHelper.IsValidMobile(request.MobileNumber) || request.Otp.Length != 6 || !request.Otp.All(char.IsDigit))
            return ResponseFactory.Failure<object>(400, "Booking reference, mobile number, and OTP are required.", "INVALID_GUEST_REQUEST");

        var cancellation = await repository.CancelGuestBookingWithOtpAsync(
            request.BookingReference.Trim(),
            MobileNumberHelper.NormalizeMobile(request.MobileNumber),
            AuthConstants.GuestPurpose(request.BookingReference),
            TokenHelper.Hash(request.Otp, settings.JwtSigningKey),
            cancellationToken);
        if (!cancellation.Success)
        {
            var statusCode = cancellation.FailureCode switch
            {
                "BOOKING_NOT_FOUND" => 404,
                "BOOKING_NOT_CANCELLABLE" => 409,
                _ => 401
            };
            return ResponseFactory.Failure<object>(statusCode, "The OTP is invalid, expired, or the booking cannot be cancelled.", cancellation.FailureCode);
        }

        return ResponseFactory.Success<object>(new { request.BookingReference, Status = "Cancelled" }, "Guest booking cancelled successfully.");
    }

    public async Task<CommonResponse<GuestBookingAccessDto>> ValidateGuestBookingAccessAsync(ValidateGuestBookingAccessRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BookingReference) || string.IsNullOrWhiteSpace(request.AccessToken))
            return ResponseFactory.Failure<GuestBookingAccessDto>(400, "Booking reference and access token are required.", "INVALID_GUEST_ACCESS_REQUEST");

        var valid = await repository.ValidateBookingAccessTokenAsync(request.BookingReference.Trim(), TokenHelper.Hash(request.AccessToken, settings.JwtSigningKey), cancellationToken);
        if (!valid)
            return ResponseFactory.Failure<GuestBookingAccessDto>(401, "Booking access token is invalid or expired.", "INVALID_BOOKING_ACCESS_TOKEN");

        var booking = await repository.GetGuestBookingAsync(request.BookingReference.Trim(), string.Empty, cancellationToken);
        if (booking is null)
            return ResponseFactory.Failure<GuestBookingAccessDto>(404, "Booking was not found.", "BOOKING_NOT_FOUND");

        return ResponseFactory.Success(new GuestBookingAccessDto { BookingReference = booking.BookingReference, BookingDateUtc = booking.BookingDateUtc, Status = booking.Status }, "Booking access validated.");
    }

    private async Task<TokenResponseDto> IssueTokensAsync(UserInfoDto user, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(ClaimTypes.MobilePhone, user.MobileNumber)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: settings.JwtIssuer,
            audience: settings.JwtAudience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSigningKey)), SecurityAlgorithms.HmacSha256)));
        var refreshToken = TokenHelper.CreateOpaqueToken();
        await repository.SaveRefreshTokenAsync(user.UserId, TokenHelper.Hash(refreshToken, settings.JwtSigningKey), now.AddDays(settings.RefreshTokenDays), cancellationToken);
        return new TokenResponseDto { AccessToken = accessToken, RefreshToken = refreshToken, AccessTokenExpiresUtc = expires, User = user };
    }

}
