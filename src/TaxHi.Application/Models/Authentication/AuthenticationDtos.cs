namespace TaxHi.Application.Models.Authentication;

public sealed class SendOtpRequest
{
    public string MobileNumber { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Login";
}

public sealed class VerifyOtpRequest
{
    public string MobileNumber { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Login";
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class RevokeTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class RefreshTokenStatus
{
    public bool IsRevoked { get; init; }
    public bool IsExpired { get; init; }
}

public class GuestCancellationOtpRequest
{
    public string BookingReference { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
}

public sealed class VerifyGuestCancellationOtpRequest : GuestCancellationOtpRequest
{
    public string Otp { get; set; } = string.Empty;
}

public sealed class ValidateGuestBookingAccessRequest
{
    public string BookingReference { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}

public sealed class UserInfoDto
{
    public long UserId { get; init; }
    public string MobileNumber { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Email { get; init; }
}

public sealed class UpdateAccountSettingsRequest
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class SessionContextResponse
{
    public string UserType { get; init; } = "Anonymous";
    public long? UserId { get; init; }
    public string? MobileNumber { get; init; }
    public bool IsGuest { get; init; }
    public long? VendorId { get; init; }
}

public sealed class TokenResponseDto
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; init; }
    public UserInfoDto User { get; init; } = new();
}

public sealed class GuestBookingAccessDto
{
    public string BookingReference { get; init; } = string.Empty;
    public DateTime BookingDateUtc { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class OtpCreateResult
{
    public bool Accepted { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public int RetryAfterSeconds { get; init; }
}

public sealed class OtpVerificationResult
{
    public bool Success { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public long? UserId { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
}

public sealed class RefreshTokenRecord
{
    public long UserId { get; init; }
    public string MobileNumber { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Email { get; init; }
}

public sealed class GuestBookingRecord
{
    public long BookingId { get; init; }
    public string BookingReference { get; init; } = string.Empty;
    public string MobileNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime BookingDateUtc { get; init; }
}

public sealed class GuestCancellationResult
{
    public bool Success { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public int CancelledCount { get; init; }
}

