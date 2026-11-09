using System.Data;
using Dapper;
using TaxHi.Application.Models.Authentication;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class AuthenticationRepository(IDbConnection connection) : IAuthenticationRepository
{
    public async Task<OtpCreateResult> CreateOtpAsync(
        string mobileNumber,
        string purpose,
        string otpHash,
        DateTime expiresUtc,
        int maxAttempts,
        int cooldownSeconds,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_CreateOTP",
            new { MobileNumber = mobileNumber, Purpose = purpose, OtpHash = otpHash, ExpiresUtc = expiresUtc, MaxAttempts = maxAttempts, CooldownSeconds = cooldownSeconds },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<OtpCreateResult>(command);
    }

    public async Task<OtpVerificationResult> VerifyOtpAsync(
        string mobileNumber,
        string purpose,
        string otpHash,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_VerifyOTP",
            new { MobileNumber = mobileNumber, Purpose = purpose, OtpHash = otpHash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<OtpVerificationResult>(command);
    }

    public async Task<UserInfoDto> RegisterOrGetUserAsync(string mobileNumber, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_RegisterOrGetUser",
            new { MobileNumber = mobileNumber },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<UserInfoDto>(command);
    }

    public async Task<UserInfoDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "usp_GetUserById",
            new { UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserInfoDto>(command);
    }

    public async Task SaveRefreshTokenAsync(long userId, string tokenHash, DateTime expiresUtc, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_SaveRefreshToken",
            new { UserId = userId, TokenHash = tokenHash, ExpiresUtc = expiresUtc },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<RefreshTokenRecord?> ValidateRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_ValidateRefreshToken",
            new { TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RefreshTokenRecord>(command);
    }

    public async Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_GetRefreshTokenStatus",
            new { TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RefreshTokenStatus>(command);
    }

    public async Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_RevokeRefreshToken",
            new { TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<GuestBookingRecord?> GetGuestBookingAsync(string bookingReference, string mobileNumber, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_GetGuestBookingByReference",
            new { BookingReference = bookingReference, MobileNumber = string.IsNullOrWhiteSpace(mobileNumber) ? null : mobileNumber },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GuestBookingRecord>(command);
    }

    public async Task SaveBookingAccessTokenAsync(long bookingId, string tokenHash, DateTime expiresUtc, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_CreateBookingAccessToken",
            new { BookingId = bookingId, TokenHash = tokenHash, ExpiresUtc = expiresUtc },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<bool> ValidateBookingAccessTokenAsync(string bookingReference, string tokenHash, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_ValidateBookingAccessToken",
            new { BookingReference = bookingReference, TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(command);
    }

    public async Task<GuestCancellationResult> CancelGuestBookingWithOtpAsync(
        string bookingReference,
        string mobileNumber,
        string purpose,
        string otpHash,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_CancelGuestBookingWithOTP",
            new
            {
                BookingReference = bookingReference,
                MobileNumber = mobileNumber,
                Purpose = purpose,
                OtpHash = otpHash
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<GuestCancellationResult>(command);
    }
}
