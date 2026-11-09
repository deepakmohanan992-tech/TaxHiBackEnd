namespace TaxHi.Application.Services;

public interface IOtpNotifier
{
    Task SendAsync(string mobileNumber, string purpose, string otp, CancellationToken cancellationToken = default);
}
