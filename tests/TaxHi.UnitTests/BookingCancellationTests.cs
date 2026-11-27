using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxHi.Application.Models.Authentication;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Services;
using TaxHi.Infrastructure.Template;

namespace TaxHi.UnitTests;

public class BookingCancellationTests
{
    private sealed record Rig(BookingService Service, FakeCancelRepo Repo, FakeAuthRepo Auth, FakeCancelEmail Email, FakeNotifier Notifier);

    private static Rig CreateRig(BookingCancellationContext? context, bool otpValid = true, bool emailThrows = false, bool auditThrows = false)
    {
        var repo = new FakeCancelRepo { Context = context };
        var auth = new FakeAuthRepo { OtpValid = otpValid };
        var email = new FakeCancelEmail { Throw = emailThrows };
        var notifier = new FakeNotifier();
        var authOptions = Options.Create(new AuthenticationOptions { JwtSigningKey = new string('k', 40) });
        var service = new BookingService(repo, null!, null!, new FakeAudit { Throw = auditThrows }, auth, notifier, authOptions,
            email, new ConfigurationBuilder().Build(), NullLogger<BookingService>.Instance);
        return new Rig(service, repo, auth, email, notifier);
    }

    private static BookingCancellationContext Ctx(double hoursToPickup, decimal paid = 1000m, string status = "Confirmed") => new()
    {
        BookingId = 5, BookingNo = "T1126-5", UserId = 7, VendorId = 3, BookingStatus = status,
        PickupDateTime = DateTime.Now.AddHours(hoursToPickup), AmountPaid = paid,
        PaymentGateway = "Razorpay", PaymentGatewayPaymentId = "pay_123"
    };

    private static CancelBookingRequest Req(string reason = "Plans changed", string otp = "123456") => new() { BookingId = 5, Reason = reason, Otp = otp };

    [Theory]
    [InlineData(48, 1000, "Pending")]       // full refund window
    [InlineData(18, 500, "Pending")]        // partial refund window (50%)
    [InlineData(2, 0, "NotApplicable")]     // too late
    public async Task Refund_is_recorded_for_manual_processing_per_the_rules(double hours, decimal expectedRefund, string expectedStatus)
    {
        var rig = CreateRig(Ctx(hours));

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.True(response.Success);
        Assert.Equal(expectedRefund, rig.Repo.RefundPassed);
        Assert.Equal(expectedStatus, response.Data!.RefundStatus);
        // The accounts email carries what accounts needs to refund by hand.
        Assert.Equal(1000m, rig.Email.LastInfo!.AmountPaid);
        Assert.Equal("pay_123", rig.Email.LastInfo.PaymentReference);
        Assert.Equal(expectedRefund, rig.Email.LastInfo.RefundAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12a456")]
    public async Task Otp_is_mandatory_and_must_be_six_digits(string otp)
    {
        var rig = CreateRig(Ctx(48));

        var response = await rig.Service.CancelBookingAsync(Req(otp: otp), 7);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("OTP_REQUIRED", response.ErrorCode);
        Assert.False(rig.Repo.CancelCalled);
    }

    [Fact]
    public async Task Wrong_otp_does_not_cancel()
    {
        var rig = CreateRig(Ctx(48), otpValid: false);

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.Equal(401, response.StatusCode);
        Assert.False(rig.Repo.CancelCalled);
        Assert.Null(rig.Email.LastInfo);
    }

    [Fact]
    public async Task Otp_is_sent_to_the_account_mobile_for_a_cancellable_booking()
    {
        var rig = CreateRig(Ctx(48));

        var response = await rig.Service.SendCancellationOtpAsync(new SendCancellationOtpRequest { BookingId = 5 }, 7);

        Assert.True(response.Success);
        Assert.Equal("BookingCancellation:5", rig.Auth.CreatedPurpose);
        Assert.Equal(1, rig.Notifier.Sent);
    }

    [Fact]
    public async Task Otp_response_returns_the_masked_mobile()
    {
        var rig = CreateRig(Ctx(48));

        var response = await rig.Service.SendCancellationOtpAsync(new SendCancellationOtpRequest { BookingId = 5 }, 7);

        Assert.Contains("******3210", System.Text.Json.JsonSerializer.Serialize(response.Data));
        Assert.StartsWith("+91 ", TaxHi.Infrastructure.Helpers.MobileNumberHelper.MaskMobile("9876543210"));
    }

    [Fact]
    public async Task Other_reason_requires_text_and_is_stored_with_it()
    {
        var rig = CreateRig(Ctx(48));

        var missing = await rig.Service.CancelBookingAsync(Req("Other"), 7);
        Assert.Equal("OTHER_REASON_REQUIRED", missing.ErrorCode);
        Assert.False(rig.Repo.CancelCalled);

        var ok = await rig.Service.CancelBookingAsync(new CancelBookingRequest { BookingId = 5, Reason = "Other", OtherReason = "Flight cancelled", Otp = "123456" }, 7);
        Assert.True(ok.Success);
        Assert.Equal("Other: Flight cancelled", rig.Repo.ReasonPassed);
    }

    [Theory]
    [InlineData("OTP_EXPIRED", "OTP expired")]
    [InlineData("OTP_INVALID", "Invalid OTP")]
    public async Task Failed_otp_gives_a_clear_message_and_does_not_cancel(string code, string message)
    {
        var rig = CreateRig(Ctx(48), otpValid: false);
        rig.Auth.FailureCode = code;

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.StartsWith(message, response.Message);
        Assert.False(rig.Repo.CancelCalled);
    }

    [Fact]
    public async Task Otp_is_not_sent_for_a_cancelled_booking()
    {
        var rig = CreateRig(Ctx(48, status: "Cancelled"));

        var response = await rig.Service.SendCancellationOtpAsync(new SendCancellationOtpRequest { BookingId = 5 }, 7);

        Assert.Equal(409, response.StatusCode);
        Assert.Equal(0, rig.Notifier.Sent);
    }

    [Fact]
    public async Task Missing_or_foreign_booking_is_404_and_nothing_is_cancelled()
    {
        var rig = CreateRig(null);

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.Equal(404, response.StatusCode);
        Assert.False(rig.Repo.CancelCalled);
    }

    [Theory]
    [InlineData("Cancelled", "ALREADY_CANCELLED")]
    [InlineData("Completed", "BOOKING_NOT_CANCELLABLE")]
    public async Task Cancelled_or_completed_bookings_are_rejected(string status, string code)
    {
        var rig = CreateRig(Ctx(48, status: status));

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.Equal(409, response.StatusCode);
        Assert.Equal(code, response.ErrorCode);
        Assert.False(rig.Repo.CancelCalled);
    }

    [Fact]
    public async Task Email_and_audit_failures_never_fail_the_cancellation()
    {
        var rig = CreateRig(Ctx(48), emailThrows: true, auditThrows: true);

        var response = await rig.Service.CancelBookingAsync(Req(), 7);

        Assert.True(response.Success);
        Assert.Equal("Cancelled", response.Data!.BookingStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reason_is_required(string reason)
    {
        var rig = CreateRig(Ctx(48));
        var response = await rig.Service.CancelBookingAsync(Req(reason), 7);
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public void Email_body_encodes_user_text_and_gives_accounts_the_refund_details()
    {
        var info = new BookingCancellationEmailInfo("<script>x</script>", "r", new DateTime(2026, 11, 24, 10, 0, 0), 500m, "Pending", 1000m, "pay_9");
        var body = BookingCancellationEmailTemplate.BuildBody(
            new BookingConfirmationEmailDto { BookingNo = "T1", CustomerName = "A" }, info,
            new BookingEmailBranding("TaxHi", null, null, null, "", "", ""));

        Assert.DoesNotContain("<script>", body);
        Assert.Contains("1,000.00", body); // amount paid; the refund rows were removed from the email by the project owner
    }

    [Fact]
    public async Task Accounts_team_is_cc_d_for_single_and_multiple_addresses()
    {
        foreach (var setting in new Dictionary<string, string?>[]
                 {
                     new() { ["BookingEmail:AccountsTeamEmails"] = "acc1@x.com" },
                     new() { ["BookingEmail:AccountsTeamEmails"] = "acc1@x.com; acc2@x.com,acc1@x.com" },
                     new() { ["BookingEmail:AccountsTeamEmails:0"] = "acc1@x.com", ["BookingEmail:AccountsTeamEmails:1"] = "acc2@x.com" }
                 })
        {
            var expected = setting.Values.SelectMany(v => v!.Split([',', ';'], StringSplitOptions.TrimEntries)).Distinct().Order().ToList();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(setting).Build();
            var service = new BookingConfirmationEmailService(null!, null!, configuration, NullLogger<BookingConfirmationEmailService>.Instance);

            var method = typeof(BookingConfirmationEmailService).GetMethod("AccountsTeamEmails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var actual = ((List<string>)method.Invoke(service, null)!).Order().ToList();

            Assert.Equal(expected, actual);
        }
        await Task.CompletedTask;
    }

    private sealed class FakeCancelRepo : IBookingRepository
    {
        public BookingCancellationContext? Context;
        public bool CancelCalled;
        public decimal RefundPassed;
        public string? ReasonPassed;

        public Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(SaveBookingPaymentRequest request, int userId, BookingAmounts amounts, string bookingNo, string tripCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(VendorConfig? Config, IReadOnlyList<VendorItem> Items)> GetVendorConfigurationAsync(int? vendorId, int? vehicleBrandId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingListResponseDto> GetBookingsAsync(int userId, string mode, int currentPage, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingPaymentContext?> GetPaymentContextAsync(long? bookingId, string? gatewayOrderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> SetGatewayOrderAsync(long bookingPaymentId, string gatewayOrderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> ConfirmGatewayPaymentAsync(string gatewayOrderId, string gatewayPaymentId, long? amountPaise, string? paymentMethod, string? eventId, string? eventType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> CancelPaymentAttemptAsync(long bookingId, long userId, string? reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ExpireLapsedHoldsAsync(int graceMinutes, int batchSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GatewayPaymentResult> FailGatewayPaymentAsync(string gatewayOrderId, string? gatewayPaymentId, string? reason, string? eventId, string? eventType, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<BookingCancellationContext?> GetCancellationContextAsync(long bookingId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Context);

        public Task<BookingCancellationResult> CancelBookingAsync(long bookingId, long userId, long? tripId, string reason, string? remarks, decimal refundAmount, CancellationToken cancellationToken = default)
        {
            CancelCalled = true;
            RefundPassed = refundAmount;
            ReasonPassed = reason;
            return Task.FromResult(new BookingCancellationResult
            {
                Outcome = "Cancelled", BookingId = bookingId, BookingNo = Context!.BookingNo, VendorId = Context.VendorId,
                PreviousStatus = Context.BookingStatus, RefundAmount = refundAmount,
                RefundStatus = refundAmount > 0 ? "Pending" : "NotApplicable", CancelledDateUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class FakeCancelEmail : IBookingConfirmationEmailService
    {
        public bool Throw;
        public BookingCancellationEmailInfo? LastInfo;
        public Task SendBookingConfirmationAsync(long bookingId, long userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendBookingCancellationAsync(long bookingId, long userId, BookingCancellationEmailInfo info, CancellationToken cancellationToken = default)
        {
            LastInfo = info;
            return Throw ? throw new InvalidOperationException("smtp down") : Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IBookingEmailRepository
    {
        public bool Throw;
        public Task<BookingConfirmationEmailDto?> GetConfirmationEmailDetailsAsync(long bookingId, long userId, CancellationToken cancellationToken = default) => Task.FromResult<BookingConfirmationEmailDto?>(null);
        public Task SaveAuditLogAsync(int vendorId, long userId, string actionName, string details, CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("audit down") : Task.CompletedTask;
    }

    private sealed class FakeNotifier : IOtpNotifier
    {
        public int Sent;
        public Task SendAsync(string mobileNumber, string purpose, string otp, CancellationToken cancellationToken = default) { Sent++; return Task.CompletedTask; }
    }

    private sealed class FakeAuthRepo : IAuthenticationRepository
    {
        public bool OtpValid = true;
        public string FailureCode = "OTP_INVALID";
        public string? CreatedPurpose;

        public Task<OtpCreateResult> CreateOtpAsync(string mobileNumber, string purpose, string otpHash, DateTime expires, int maxAttempts, int cooldownSeconds, CancellationToken cancellationToken = default)
        { CreatedPurpose = purpose; return Task.FromResult(new OtpCreateResult { Accepted = true, Expires = expires }); }

        public Task<OtpVerificationResult> VerifyOtpAsync(string mobileNumber, string purpose, string otpHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OtpVerificationResult { Success = OtpValid, FailureCode = OtpValid ? "" : FailureCode });

        public Task<UserInfoDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserInfoDto?>(new UserInfoDto { UserId = userId, MobileNumber = "9876543210" });

        public Task<UserInfoDto> RegisterOrGetUserAsync(string mobileNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserInfoDto?> UpdateUserAsync(long userId, string mobileNumber, string name, string? email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserInfoDto?> GetAuthUserDetailsAsync(long? userId, string? mobileNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SaveRefreshTokenAsync(long userId, string tokenHash, DateTime expires, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RefreshTokenRecord?> ValidateRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string tokenHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GuestBookingRecord?> GetGuestBookingAsync(string bookingReference, string mobileNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SaveBookingAccessTokenAsync(long bookingId, string tokenHash, DateTime expires, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ValidateBookingAccessTokenAsync(string bookingReference, string tokenHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GuestCancellationResult> CancelGuestBookingWithOtpAsync(string bookingReference, string mobileNumber, string purpose, string otpHash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
