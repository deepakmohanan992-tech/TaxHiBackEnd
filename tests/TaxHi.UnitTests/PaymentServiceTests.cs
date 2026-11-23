using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Services;

namespace TaxHi.UnitTests;

public class PaymentServiceTests
{
    private const string KeySecret = "test_key_secret";
    private const string WebhookSecret = "test_webhook_secret";

    private static string Sign(string payload, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private sealed class Fixture
    {
        public FakeRepo Repo = new();
        public FakeBookingService Bookings = new();
        public FakeHandler Http = new();
        public FakeInvoices Invoices = new();
        public FakeEmails Emails = new();
        public PaymentService Service;

        public Fixture(string keyId = "rzp_test_abc")
        {
            Service = new PaymentService(
                new HttpClient(Http) { BaseAddress = new Uri("https://api.razorpay.test/v1/") },
                Bookings, Repo, Invoices, Emails,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Razorpay:KeyId"] = keyId, ["Razorpay:KeySecret"] = KeySecret, ["Razorpay:WebhookSecret"] = WebhookSecret, ["Razorpay:ApiBaseUrl"] = "https://api.razorpay.test/v1/" }).Build(),
                NullLogger<PaymentService>.Instance);
        }
    }

    // ------------------------------------------------------------ create order
    [Theory]
    [InlineData("FullPay", 12000.50, 1200050)]
    [InlineData("PartPay", 3000.13, 300013)]
    public async Task CreateOrder_UsesServerStoredAmountInPaise(string option, double payable, long expectedPaise)
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(paymentAmount: (decimal)payable);
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest { PaymentOption = option, PayableNowAmount = 1m }, 7);

        Assert.True(response.Success);
        Assert.Equal(expectedPaise, response.Data!.Amount);       // client-sent PayableNowAmount (1) is ignored
        Assert.Equal("INR", response.Data.Currency);
        Assert.Equal("rzp_test_abc", response.Data.KeyId);
        Assert.Equal("order_NEW1", response.Data.OrderId);
        Assert.Equal("T1126-1", response.Data.BookingReference);
        Assert.Contains($"\"amount\":{expectedPaise}", f.Http.LastBody);
        Assert.Contains("\"receipt\":\"T1126-1\"", f.Http.LastBody);
    }

    [Fact]
    public async Task CreateOrder_DuplicateRequest_ReusesExistingOrder()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_EXISTING");
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);

        Assert.Equal("order_EXISTING", response.Data!.OrderId);
        Assert.Equal(0, f.Http.Calls);                              // no second Razorpay order
    }

    [Fact]
    public async Task CreateOrder_RaceLoser_ReturnsWinningOrder()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx();
        f.Repo.WinningOrderId = "order_WINNER";
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal("order_WINNER", response.Data!.OrderId);
    }

    [Fact]
    public async Task CreateOrder_ForeignBooking_Is404()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(userId: 99);
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal(404, response.StatusCode);
    }

    [Theory]
    [InlineData("Success", "Pending", "ALREADY_PAID")]
    [InlineData("Pending", "Confirmed", "ALREADY_PAID")]
    [InlineData("Pending", "Cancelled", "BOOKING_NOT_PAYABLE")]
    public async Task CreateOrder_NotPayable_Is409(string payment, string booking, string code)
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(paymentStatus: payment, bookingStatus: booking);
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal(409, response.StatusCode);
        Assert.Equal(code, response.ErrorCode);
    }

    [Fact]
    public async Task CreateOrder_ExpiredHold_Is409()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(hold: DateTime.Now.AddMinutes(-1));
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal("BOOKING_HOLD_EXPIRED", response.ErrorCode);
    }

    [Fact]
    public async Task CreateOrder_GatewayDown_Is502_AndNothingStored()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx();
        f.Http.Status = HttpStatusCode.InternalServerError;
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal(502, response.StatusCode);
        Assert.Equal(0, f.Repo.SetOrderCalls);
    }

    [Fact]
    public async Task CreateOrder_BookingValidationFailure_IsPassedThrough()
    {
        var f = new Fixture();
        f.Bookings.Result = new CommonResponse<SaveBookingPaymentResponse> { Success = false, StatusCode = 400, Message = "bad", ErrorCode = "TOTAL_FARE_MISMATCH" };
        var response = await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7);
        Assert.Equal("TOTAL_FARE_MISMATCH", response.ErrorCode);
        Assert.Equal(0, f.Http.Calls);
    }

    [Fact]
    public async Task CreateOrder_VehicleAlreadyReserved_IsPassedThrough()
    {
        var f = new Fixture();
        f.Bookings.Result = new CommonResponse<SaveBookingPaymentResponse> { Success = false, StatusCode = 400, ErrorCode = "VEHICLE_ALREADY_RESERVED" };
        Assert.Equal("VEHICLE_ALREADY_RESERVED", (await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7)).ErrorCode);
    }

    [Fact]
    public async Task CreateOrder_NotConfigured_Is503()
    {
        var f = new Fixture(keyId: "");
        Assert.Equal(503, (await f.Service.CreateOrderAsync(new SaveBookingPaymentRequest(), 7)).StatusCode);
    }

    // ------------------------------------------------------------ verify
    [Fact]
    public async Task Verify_InvalidSignature_IsRejected_AndNothingConfirmed()
    {
        var f = new Fixture();
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_X", KeySecret)), 7);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("INVALID_PAYMENT_SIGNATURE", response.ErrorCode);
        Assert.Equal(0, f.Repo.ConfirmCalls);
    }

    [Theory]
    [InlineData("not-hex")]
    [InlineData("")]
    public async Task Verify_MalformedSignature_IsRejected(string signature)
    {
        var f = new Fixture();
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", signature), 7);
        Assert.False(response.Success);
        Assert.Equal(0, f.Repo.ConfirmCalls);
    }

    [Fact]
    public async Task Verify_SignedWithWrongSecret_IsRejected()
    {
        var f = new Fixture();
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_1", "other")), 7);
        Assert.Equal("INVALID_PAYMENT_SIGNATURE", response.ErrorCode);
    }

    [Fact]
    public async Task Verify_ValidSignature_ConfirmsBookingWithoutClientAmount()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_1");
        f.Repo.Confirm = Result("Confirmed");
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_1", KeySecret)), 7);

        Assert.True(response.Success);
        Assert.False(response.Data!.AlreadyProcessed);
        Assert.Equal("Confirmed", response.Data.BookingStatus);
        Assert.Null(f.Repo.LastAmountPaise);
        Assert.True(f.Invoices.LastRegenerate);
        Assert.Equal(1, f.Emails.Sent);
    }

    [Fact]
    public async Task Verify_Duplicate_IsIdempotent()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_1");
        f.Repo.Confirm = Result("AlreadyProcessed");
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_1", KeySecret)), 7);

        Assert.True(response.Success);
        Assert.True(response.Data!.AlreadyProcessed);
        Assert.False(f.Invoices.LastRegenerate);
        Assert.Equal(0, f.Emails.Sent);
    }

    [Fact]
    public async Task Verify_OrderOfAnotherUser_Is404_AndNotConfirmed()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_1", userId: 99);
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_1", KeySecret)), 7);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal(0, f.Repo.ConfirmCalls);
    }

    [Fact]
    public async Task Verify_DifferentPaymentOnPaidOrder_Is409()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_1");
        f.Repo.Confirm = Result("PaymentIdConflict");
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_2", Sign("order_1|pay_2", KeySecret)), 7);
        Assert.Equal(409, response.StatusCode);
    }

    [Fact]
    public async Task Verify_VehicleLostAfterHoldExpiry_ReportsRefundRequired()
    {
        var f = new Fixture();
        f.Repo.Context = Ctx(orderId: "order_1");
        f.Repo.Confirm = Result("RefundRequired");
        var response = await f.Service.VerifyPaymentAsync(Verify("order_1", "pay_1", Sign("order_1|pay_1", KeySecret)), 7);
        Assert.Equal("PAYMENT_RECEIVED_BOOKING_UNAVAILABLE", response.ErrorCode);
        Assert.Equal(0, f.Emails.Sent);
    }

    // ------------------------------------------------------------ webhook
    private static string Body(string evt, string order = "order_1", string payment = "pay_1", long amount = 300013) =>
        "{\"event\":\"" + evt + "\",\"payload\":{\"payment\":{\"entity\":{\"id\":\"" + payment + "\",\"order_id\":\"" + order +
        "\",\"amount\":" + amount + ",\"method\":\"upi\",\"error_description\":\"declined\"}}}}";

    [Fact]
    public async Task Webhook_InvalidSignature_Is401_AndNothingProcessed()
    {
        var f = new Fixture();
        var body = Body("payment.captured");
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, "wrong"), "evt_1");
        Assert.Equal(401, response.StatusCode);
        Assert.Equal(0, f.Repo.ConfirmCalls);
    }

    [Theory]
    [InlineData("payment.captured")]
    [InlineData("order.paid")]
    public async Task Webhook_Captured_ConfirmsWithAmountAndEventId(string evt)
    {
        var f = new Fixture();
        f.Repo.Confirm = Result("Confirmed");
        var body = Body(evt);
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, WebhookSecret), "evt_1");

        Assert.True(response.Success);
        Assert.Equal(300013, f.Repo.LastAmountPaise);
        Assert.Equal("evt_1", f.Repo.LastEventId);
        Assert.Equal("upi", f.Repo.LastMethod);
        Assert.True(f.Invoices.LastRegenerate);
        Assert.Equal(1, f.Emails.Sent);
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_IsAcknowledged()
    {
        var f = new Fixture();
        f.Repo.Confirm = Result("DuplicateEvent");
        var body = Body("payment.captured");
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, WebhookSecret), "evt_1");
        Assert.True(response.Success);
        Assert.Equal("DuplicateEvent", response.Data);
    }

    [Fact]
    public async Task Webhook_Failed_RecordsFailure()
    {
        var f = new Fixture();
        f.Repo.Fail = Result("Failed");
        var body = Body("payment.failed");
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, WebhookSecret), "evt_2");
        Assert.True(response.Success);
        Assert.Equal(1, f.Repo.FailCalls);
        Assert.Equal(0, f.Repo.ConfirmCalls);
        Assert.Equal(0, f.Emails.Sent);
    }

    [Fact]
    public async Task Webhook_UnknownEvent_IsIgnored()
    {
        var f = new Fixture();
        var body = Body("refund.created");
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, WebhookSecret), "evt_3");
        Assert.True(response.Success);
        Assert.Equal(0, f.Repo.ConfirmCalls + f.Repo.FailCalls);
    }

    [Fact]
    public async Task Webhook_AmountMismatch_IsAcknowledgedNotRetried()
    {
        var f = new Fixture();
        f.Repo.Confirm = Result("AmountMismatch");
        var body = Body("payment.captured", amount: 1);
        var response = await f.Service.HandleWebhookAsync(body, Sign(body, WebhookSecret), "evt_4");
        Assert.True(response.Success);
        Assert.Equal("AmountMismatch", response.Data);
    }

    // ------------------------------------------------------------ helpers / fakes
    private static VerifyRazorpayPaymentRequest Verify(string order, string payment, string sig) =>
        new() { RazorpayOrderId = order, RazorpayPaymentId = payment, RazorpaySignature = sig };

    private static GatewayPaymentResult Result(string outcome) =>
        new() { Outcome = outcome, BookingId = 1, UserId = 7, BookingNo = "T1126-1", BookingStatus = "Confirmed", PaymentStatus = "Success", AmountPaid = 3000.13m };

    private static BookingPaymentContext Ctx(decimal paymentAmount = 3000.13m, string? orderId = null, int userId = 7,
        string paymentStatus = "Pending", string bookingStatus = "Pending", DateTime? hold = null) => new()
    {
        BookingId = 1, BookingNo = "T1126-1", UserId = userId, BookingStatus = bookingStatus,
        HoldExpiresAt = hold ?? DateTime.Now.AddMinutes(10), PaymentAmount = paymentAmount, PaymentStatus = paymentStatus,
        BookingPaymentId = 11, PaymentGatewayOrderId = orderId
    };

    private sealed class FakeBookingService : IBookingService
    {
        public CommonResponse<SaveBookingPaymentResponse> Result = new() { Success = true, StatusCode = 200, Data = new SaveBookingPaymentResponse { BookingId = 1 } };
        public Task<CommonResponse<SaveBookingPaymentResponse>> SaveBookingPaymentAsync(SaveBookingPaymentRequest request, int userId, CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<CommonResponse<BookingListResponseDto>> GetBookingsAsync(int userId, string? mode, int currentPage, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeEmails : IBookingConfirmationEmailService
    {
        public int Sent;
        public Task SendBookingConfirmationAsync(long bookingId, long userId, CancellationToken cancellationToken = default)
        { Sent++; return Task.CompletedTask; }
    }

    private sealed class FakeInvoices : IBookingInvoiceService
    {
        public bool LastRegenerate;
        public Task<BookingInvoiceResult> EnsureInvoiceAsync(long bookingId, long userId, string? bookingNo = null, bool regenerate = false, CancellationToken cancellationToken = default)
        { LastRegenerate = regenerate; return Task.FromResult(new BookingInvoiceResult { BookingId = bookingId, InvoiceGenerated = true, InvoiceUrl = "/x" }); }
        public string? ResolveExistingInvoicePath(string? storedRelativePath) => null;
        public Task<CommonResponse<BookingInvoiceResult>> RetryInvoiceAsync(long bookingId, long userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CommonResponse<BookingInvoiceFile>> GetInvoiceFileAsync(long bookingId, long userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls; public string LastBody = ""; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"id\":\"order_NEW1\",\"status\":\"created\"}") };
        }
    }

    private sealed class FakeRepo : IBookingRepository
    {
        public BookingPaymentContext? Context; public string? WinningOrderId;
        public GatewayPaymentResult Confirm = Result("Confirmed"), Fail = Result("Failed");
        public int ConfirmCalls, FailCalls, SetOrderCalls; public long? LastAmountPaise; public string? LastEventId, LastMethod;

        public Task<BookingPaymentContext?> GetPaymentContextAsync(long? bookingId, string? gatewayOrderId, CancellationToken cancellationToken = default) => Task.FromResult(Context);
        public Task<string> SetGatewayOrderAsync(long bookingPaymentId, string gatewayOrderId, CancellationToken cancellationToken = default) { SetOrderCalls++; return Task.FromResult(WinningOrderId ?? gatewayOrderId); }
        public Task<GatewayPaymentResult> ConfirmGatewayPaymentAsync(string gatewayOrderId, string gatewayPaymentId, long? amountPaise, string? paymentMethod, string? eventId, string? eventType, CancellationToken cancellationToken = default)
        { ConfirmCalls++; LastAmountPaise = amountPaise; LastEventId = eventId; LastMethod = paymentMethod; return Task.FromResult(Confirm); }
        public Task<GatewayPaymentResult> FailGatewayPaymentAsync(string gatewayOrderId, string? gatewayPaymentId, string? reason, string? eventId, string? eventType, CancellationToken cancellationToken = default) { FailCalls++; return Task.FromResult(Fail); }
        public Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(SaveBookingPaymentRequest request, int userId, BookingAmounts amounts, string bookingNo, string tripCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BookingListResponseDto> GetBookingsAsync(int userId, string mode, int currentPage, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
