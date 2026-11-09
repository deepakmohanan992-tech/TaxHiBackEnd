namespace TaxHi.Application.Services;

public sealed class AuthenticationOptions
{
    public string JwtIssuer { get; set; } = string.Empty;
    public string JwtAudience { get; set; } = string.Empty;
    public string JwtSigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int OtpExpiryMinutes { get; set; } = 5;
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpResendCooldownSeconds { get; set; } = 60;
    public int GuestAccessTokenDays { get; set; } = 1;
}
