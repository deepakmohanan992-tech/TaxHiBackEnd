using TaxHi.Application.Models.Authentication;

namespace TaxHi.Application.Repositories;

public interface IAuthenticationRepository
{
    Task<OtpCreateResult> CreateOtpAsync(string mobileNumber, string purpose, string otpHash, DateTime expiresUtc, int maxAttempts, int cooldownSeconds, CancellationToken cancellationToken = default);
    Task<OtpVerificationResult> VerifyOtpAsync(string mobileNumber, string purpose, string otpHash, CancellationToken cancellationToken = default);
    Task<UserInfoDto> RegisterOrGetUserAsync(string mobileNumber, CancellationToken cancellationToken = default);
    Task<UserInfoDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default);
    Task SaveRefreshTokenAsync(long userId, string tokenHash, DateTime expiresUtc, CancellationToken cancellationToken = default);
    Task<RefreshTokenRecord?> ValidateRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<GuestBookingRecord?> GetGuestBookingAsync(string bookingReference, string mobileNumber, CancellationToken cancellationToken = default);
    Task SaveBookingAccessTokenAsync(long bookingId, string tokenHash, DateTime expiresUtc, CancellationToken cancellationToken = default);
    Task<bool> ValidateBookingAccessTokenAsync(string bookingReference, string tokenHash, CancellationToken cancellationToken = default);
Task<GuestCancellationResult> CancelGuestBookingWithOtpAsync(string bookingReference, string mobileNumber, string purpose, string otpHash, CancellationToken cancellationToken = default);
}
