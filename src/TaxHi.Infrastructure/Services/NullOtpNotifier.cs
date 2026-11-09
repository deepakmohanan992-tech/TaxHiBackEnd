using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Services;

public sealed class NullOtpNotifier : IOtpNotifier
{
    public Task SendAsync(string mobileNumber, string purpose, string otp, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
