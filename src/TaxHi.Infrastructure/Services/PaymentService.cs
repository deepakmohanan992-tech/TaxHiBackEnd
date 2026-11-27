using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// Razorpay Standard Checkout. Booking persistence stays in <see cref="IBookingService"/> / <see cref="IBookingRepository"/>;
/// this service only creates the gateway order, verifies signatures and hands verified payments to the repository.
/// The amount charged is always the one stored by the backend, never a value sent by the client.
/// </summary>
public sealed class PaymentService(
    HttpClient httpClient,
    IBookingService bookingService,
    IBookingRepository repository,
    IBookingInvoiceService invoiceService,
    IBookingConfirmationEmailService confirmationEmailService,
    IConfiguration configuration,
    ILogger<PaymentService> logger) : IPaymentService
{
    private const string Currency = "INR";
    private const int MaxIdLength = 64;
    private string KeyId => configuration["Razorpay:KeyId"] ?? string.Empty;
    private string KeySecret => configuration["Razorpay:KeySecret"] ?? string.Empty;
    private string WebhookSecret => configuration["Razorpay:WebhookSecret"] ?? string.Empty;

    // Base address, timeout and Basic auth come from the "Razorpay" section of appsettings.Development.json.
    private readonly HttpClient _client = ConfigureClient(httpClient, configuration);

    private static HttpClient ConfigureClient(HttpClient client, IConfiguration configuration)
    {
        var baseUrl = configuration["Razorpay:ApiBaseUrl"] is { Length: > 0 } u ? u : "https://api.razorpay.com/v1/";
        client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        client.Timeout = ApiSettings.HttpTimeout(configuration, "Razorpay");
        var keyId = configuration["Razorpay:KeyId"];
        if (!string.IsNullOrEmpty(keyId))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{configuration["Razorpay:KeySecret"]}")));
        return client;
    }

    // ------------------------------------------------------------------ create order

    public async Task<CommonResponse<RazorpayOrderResponse>> CreateOrderAsync(
        SaveBookingPaymentRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status503ServiceUnavailable,
                "Online payment is not available right now.", "PAYMENT_NOT_CONFIGURED");

        // Existing flow: validates the fare breakup server-side, takes the vehicle lock, holds the vehicle and is
        // idempotent on ClientRequestId, so retries and double-clicks return the same booking and payment row.
        var saved = await bookingService.SaveBookingPaymentAsync(request, userId, cancellationToken);
        if (!saved.Success || saved.Data is null)
            return ResponseFactory.Failure<RazorpayOrderResponse>(saved.StatusCode, saved.Message, saved.ErrorCode ?? "BOOKING_SAVE_FAILED");

        try
        {
            var context = await repository.GetPaymentContextAsync(saved.Data.BookingId, null, cancellationToken);
            // Same answer for "not yours" and "missing" so booking ids cannot be probed.
            if (context is null || context.UserId != userId)
                return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");

            if (context.PaymentStatus == "Success" || context.BookingStatus == "Confirmed")
                return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status409Conflict, "This booking is already paid.", "ALREADY_PAID");
            if (context.BookingStatus != "Pending")
                return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status409Conflict, "This booking can no longer be paid for.", "BOOKING_NOT_PAYABLE");
            if (context.HoldExpiresAt is { } hold && hold <= DateTime.Now)
                return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status409Conflict,
                    "The payment window for this booking has expired. Please start a new booking.", "BOOKING_HOLD_EXPIRED");

            var amountPaise = ToPaise(context.PaymentAmount);
            if (amountPaise < 100) // Razorpay minimum is INR 1.00
                return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status400BadRequest, "The payable amount is invalid.", "INVALID_PAYABLE_AMOUNT");

            // Reuse the order of a previous attempt (duplicate request / customer retry) instead of creating another.
            var orderId = context.PaymentGatewayOrderId;
            if (string.IsNullOrEmpty(orderId))
            {
                var created = await CreateGatewayOrderAsync(amountPaise, context, cancellationToken);
                // If two requests raced, the first stored order wins and the loser's unused order simply expires.
                orderId = await repository.SetGatewayOrderAsync(context.BookingPaymentId, created, cancellationToken);
            }

            return ResponseFactory.Success(new RazorpayOrderResponse
            {
                KeyId = KeyId,
                OrderId = orderId,
                Amount = amountPaise,
                Currency = Currency,
                BookingReference = context.BookingNo,
                BookingId = context.BookingId,
                HoldExpiresAt = context.HoldExpiresAt
            }, "Payment order created successfully.");
        }
        catch (RazorpayApiException ex)
        {
            logger.LogError(ex, "Razorpay order creation failed for booking {BookingId}.", saved.Data.BookingId);
            return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status502BadGateway,
                "Unable to start the payment right now. Please try again.", "PAYMENT_GATEWAY_ERROR");
        }
        catch (SqlException ex) when (SqlErrors.IsTimeout(ex) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Database timeout (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status504GatewayTimeout,
                "The request took too long. Please try again.", "REQUEST_TIMEOUT");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while creating payment order (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<RazorpayOrderResponse>(StatusCodes.Status500InternalServerError,
                "Unable to start the payment right now. Please try again.", "PAYMENT_ORDER_FAILED");
        }
    }

    // ------------------------------------------------------------------ verify (checkout callback)

    public async Task<CommonResponse<PaymentVerificationResponse>> VerifyPaymentAsync(
        VerifyRazorpayPaymentRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status503ServiceUnavailable,
                "Online payment is not available right now.", "PAYMENT_NOT_CONFIGURED");

        if (!IsValidId(request.RazorpayOrderId) || !IsValidId(request.RazorpayPaymentId) || string.IsNullOrWhiteSpace(request.RazorpaySignature))
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status400BadRequest,
                "razorpay_order_id, razorpay_payment_id and razorpay_signature are required.", "INVALID_PAYMENT_DETAILS");

        // Nothing is read or written until the signature checks out.
        if (!IsValidCheckoutSignature(request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature))
        {
            logger.LogWarning("Invalid Razorpay signature for order {OrderId} from user {UserId}.", request.RazorpayOrderId, userId);
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status400BadRequest,
                "Payment verification failed.", "INVALID_PAYMENT_SIGNATURE");
        }

        try
        {
            // The order must belong to a payment attempt of this user's booking.
            var context = await repository.GetPaymentContextAsync(null, request.RazorpayOrderId, cancellationToken);
            if (context is null || context.UserId != userId)
                return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status404NotFound, "Payment order not found.", "PAYMENT_ORDER_NOT_FOUND");

            var result = await repository.ConfirmGatewayPaymentAsync(
                request.RazorpayOrderId, request.RazorpayPaymentId, amountPaise: null, paymentMethod: null,
                eventId: null, eventType: "checkout.verify", cancellationToken);

            switch (result.Outcome)
            {
                case "Confirmed":
                case "AlreadyProcessed":
                    var alreadyProcessed = result.Outcome == "AlreadyProcessed";
                    var invoice = await invoiceService.EnsureInvoiceAsync(
                        result.BookingId, result.UserId, result.BookingNo, regenerate: !alreadyProcessed, cancellationToken: CancellationToken.None);
                    // Only the call that actually confirmed the payment sends the email; a repeat verify or the
                    // webhook arriving later sees "AlreadyProcessed" and does not send a duplicate.
                    if (!alreadyProcessed)
                        await SendConfirmationEmailAsync(result.BookingId, result.UserId, CancellationToken.None);
                    return ResponseFactory.Success(new PaymentVerificationResponse
                    {
                        BookingId = result.BookingId,
                        BookingNo = result.BookingNo,
                        BookingStatus = result.BookingStatus ?? string.Empty,
                        PaymentStatus = result.PaymentStatus ?? string.Empty,
                        AmountPaid = result.AmountPaid,
                        BalancePayableAmount = result.BalancePayableAmount,
                        AlreadyProcessed = alreadyProcessed,
                        InvoiceGenerated = invoice.InvoiceGenerated,
                        InvoiceUrl = invoice.InvoiceUrl
                    }, alreadyProcessed ? "Payment was already verified." : "Payment verified and booking confirmed.");

                case "RefundRequired":
                    logger.LogError("Payment {PaymentId} received for booking {BookingId} but the booking could not be confirmed; refund required.",
                        request.RazorpayPaymentId, result.BookingId);
                    return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status409Conflict,
                        "Your payment was received but the vehicle is no longer available. A refund will be processed.", "PAYMENT_RECEIVED_BOOKING_UNAVAILABLE");

                case "PaymentIdConflict":
                    logger.LogWarning("Order {OrderId} already settled with a different payment id than {PaymentId}.", request.RazorpayOrderId, request.RazorpayPaymentId);
                    return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status409Conflict,
                        "This order has already been paid.", "ORDER_ALREADY_PAID");

                case "OrderNotFound":
                    return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status404NotFound, "Payment order not found.", "PAYMENT_ORDER_NOT_FOUND");

                default:
                    return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status409Conflict,
                        "This payment can no longer be processed.", "PAYMENT_NOT_PROCESSABLE");
            }
        }
        catch (SqlException ex) when (ex.Number is 50002 or 1205) // vehicle lock timeout / deadlock victim; safe to retry
        {
            logger.LogWarning(ex, "Lock contention while confirming payment (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status409Conflict,
                "Your payment is being processed. Please try again in a moment.", "PAYMENT_LOCK_TIMEOUT");
        }
        catch (SqlException ex) when (SqlErrors.IsTimeout(ex) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Database timeout (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status504GatewayTimeout,
                "Your payment is being processed and the request took too long. Please check your booking status or try again in a moment.", "REQUEST_TIMEOUT");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while confirming payment (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<PaymentVerificationResponse>(StatusCodes.Status500InternalServerError,
                "Unable to confirm the payment right now. Please try again.", "PAYMENT_CONFIRM_FAILED");
        }
    }

    // ------------------------------------------------------------------ cancel / abandon

    public async Task<CommonResponse<PaymentCancellationResponse>> CancelPaymentAsync(
        CancelPaymentRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED");
        if (request is null || request.BookingId is <= 0 or > int.MaxValue)
            return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status400BadRequest, "A valid BookingId is required.", "INVALID_BOOKING_ID");

        try
        {
            var context = await repository.GetPaymentContextAsync(request.BookingId, null, cancellationToken);
            // Same answer for "not yours" and "missing" so booking ids cannot be probed.
            if (context is null || context.UserId != userId)
                return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");

            // A cancel from the browser is never taken at its word: if the gateway shows the order was paid,
            // the payment is confirmed (re-validating the vehicle) instead of the hold being released.
            if (context.PaymentStatus != "Success" && context.BookingStatus == "Pending"
                && IsConfigured && !string.IsNullOrEmpty(context.PaymentGatewayOrderId))
            {
                var captured = await FindCapturedPaymentAsync(context.PaymentGatewayOrderId, cancellationToken);
                if (captured is not null)
                {
                    // The money is real, so this database work is not tied to the request token.
                    var confirmed = await repository.ConfirmGatewayPaymentAsync(
                        context.PaymentGatewayOrderId, captured.Id, captured.AmountPaise, captured.Method,
                        eventId: null, eventType: "checkout.cancel-check", CancellationToken.None);

                    if (confirmed.Outcome == "Confirmed")
                    {
                        await invoiceService.EnsureInvoiceAsync(confirmed.BookingId, confirmed.UserId, confirmed.BookingNo, regenerate: true, cancellationToken: CancellationToken.None);
                        await SendConfirmationEmailAsync(confirmed.BookingId, confirmed.UserId, CancellationToken.None);
                    }

                    if (confirmed.Outcome is "Confirmed" or "AlreadyProcessed")
                        return ResponseFactory.Success(ToCancellation(confirmed, released: false, alreadyPaid: true),
                            "The payment was already completed, so the booking is confirmed.");

                    if (confirmed.Outcome == "RefundRequired")
                    {
                        logger.LogError("Payment {PaymentId} found for booking {BookingId} during cancel but the vehicle is no longer available; refund required.",
                            captured.Id, confirmed.BookingId);
                        return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status409Conflict,
                            "Your payment was received but the vehicle is no longer available. A refund will be processed.", "PAYMENT_RECEIVED_BOOKING_UNAVAILABLE");
                    }
                }
            }

            var result = await repository.CancelPaymentAttemptAsync(context.BookingId, userId, request.Reason, cancellationToken);
            switch (result.Outcome)
            {
                case "Released":
                case "AlreadyReleased":
                    return ResponseFactory.Success(ToCancellation(result, released: true, alreadyPaid: false),
                        "The payment was cancelled and the vehicle has been released.");
                case "AlreadyPaid":
                    return ResponseFactory.Success(ToCancellation(result, released: false, alreadyPaid: true),
                        "This booking is already paid and confirmed.");
                case "NotFound":
                    return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");
                default:
                    return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status409Conflict,
                        "This booking can no longer be changed.", "BOOKING_NOT_PAYABLE");
            }
        }
        catch (SqlException ex) when (SqlErrors.IsTimeout(ex) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Database timeout while cancelling payment for booking {BookingId}.", request.BookingId);
            return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status504GatewayTimeout,
                "The request took too long. Please try again.", "REQUEST_TIMEOUT");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while cancelling payment (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<PaymentCancellationResponse>(StatusCodes.Status500InternalServerError,
                "Unable to cancel the payment right now. Please try again.", "PAYMENT_CANCEL_FAILED");
        }
    }

    private static PaymentCancellationResponse ToCancellation(GatewayPaymentResult r, bool released, bool alreadyPaid) => new()
    {
        BookingId = r.BookingId,
        BookingNo = r.BookingNo,
        BookingStatus = r.BookingStatus ?? string.Empty,
        PaymentStatus = r.PaymentStatus ?? string.Empty,
        Released = released,
        AlreadyPaid = alreadyPaid
    };

    private sealed record CapturedPayment(string Id, long AmountPaise, string? Method);

    /// <summary>Asks Razorpay (server to server) for a captured payment on the order. Null when none, or when the lookup fails.</summary>
    private async Task<CapturedPayment?> FindCapturedPaymentAsync(string orderId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync($"orders/{Uri.EscapeDataString(orderId)}/payments", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Razorpay returned {Status} while checking order {OrderId} before releasing the hold.", (int)response.StatusCode, orderId);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in items.EnumerateArray())
            {
                var id = Str(item, "id");
                if (Str(item, "status") == "captured" && IsValidId(id) && item.TryGetProperty("amount", out var a) && a.TryGetInt64(out var amount))
                {
                    var method = Str(item, "method");
                    return new CapturedPayment(id, amount, method.Length == 0 ? null : method);
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The hold is still released; a payment that really was in flight is re-validated when its callback arrives.
            logger.LogWarning(ex, "Could not check Razorpay order {OrderId} before releasing the hold.", orderId);
            return null;
        }
    }

    // ------------------------------------------------------------------ webhook

    public async Task<CommonResponse<string>> HandleWebhookAsync(
        string rawBody, string? signature, string? eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(WebhookSecret))
            return ResponseFactory.Failure<string>(StatusCodes.Status503ServiceUnavailable, "Webhook is not configured.", "WEBHOOK_NOT_CONFIGURED");

        if (string.IsNullOrWhiteSpace(signature) || !IsValidSignature(rawBody, WebhookSecret, signature))
        {
            logger.LogWarning("Rejected Razorpay webhook with an invalid signature.");
            return ResponseFactory.Failure<string>(StatusCodes.Status401Unauthorized, "Invalid webhook signature.", "INVALID_WEBHOOK_SIGNATURE");
        }

        string eventType, orderId, paymentId, method, failure;
        long? amountPaise;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            eventType = Str(root, "event");
            var payload = root.TryGetProperty("payload", out var p) ? p : default;
            var payment = Entity(payload, "payment");
            var order = Entity(payload, "order");

            paymentId = Str(payment, "id");
            orderId = Str(payment, "order_id");
            if (orderId.Length == 0) orderId = Str(order, "id");
            method = Str(payment, "method");
            failure = Str(payment, "error_description");
            amountPaise = payment.ValueKind == JsonValueKind.Object && payment.TryGetProperty("amount", out var a) && a.TryGetInt64(out var v) ? v
                : order.ValueKind == JsonValueKind.Object && order.TryGetProperty("amount_paid", out var ap) && ap.TryGetInt64(out var v2) ? v2
                : null;
        }
        catch (JsonException)
        {
            return ResponseFactory.Failure<string>(StatusCodes.Status400BadRequest, "Malformed webhook payload.", "INVALID_WEBHOOK_PAYLOAD");
        }

        if (eventType is not ("payment.captured" or "order.paid" or "payment.failed"))
            return ResponseFactory.Success(eventType, "Event ignored.");
        if (!IsValidId(orderId) || (eventType != "payment.failed" && !IsValidId(paymentId)) || (eventId is { Length: > 100 }))
            return ResponseFactory.Failure<string>(StatusCodes.Status400BadRequest, "Webhook payload is missing order or payment details.", "INVALID_WEBHOOK_PAYLOAD");

        try
        {
            if (eventType == "payment.failed")
            {
                var failed = await repository.FailGatewayPaymentAsync(orderId, paymentId.Length == 0 ? null : paymentId,
                    failure.Length == 0 ? "Payment failed" : failure, eventId, eventType, cancellationToken);
                return Acknowledge(failed, eventType, orderId);
            }

            var result = await repository.ConfirmGatewayPaymentAsync(
                orderId, paymentId, amountPaise, method.Length == 0 ? null : method, eventId, eventType, cancellationToken);

            if (result.Outcome == "Confirmed")
            {
                await invoiceService.EnsureInvoiceAsync(result.BookingId, result.UserId, result.BookingNo, regenerate: true, cancellationToken: CancellationToken.None);
                await SendConfirmationEmailAsync(result.BookingId, result.UserId, CancellationToken.None);
            }

            return Acknowledge(result, eventType, orderId);
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Non-2xx makes Razorpay redeliver; the event row rolled back with the transaction, so the retry reprocesses.
            logger.LogError(ex, "Database error while processing Razorpay webhook {EventType} for order {OrderId} (SQL error {Number}).", eventType, orderId, ex.Number);
            return ResponseFactory.Failure<string>(StatusCodes.Status500InternalServerError, "Webhook processing failed.", "WEBHOOK_PROCESSING_FAILED");
        }
    }

    /// <summary>Best effort: the payment is already confirmed, so an email problem must never change the outcome.</summary>
    private async Task SendConfirmationEmailAsync(long bookingId, long userId, CancellationToken cancellationToken)
    {
        try
        {
            await confirmationEmailService.SendBookingConfirmationAsync(bookingId, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Confirmation email step failed for booking {BookingId}.", bookingId);
        }
    }

    private CommonResponse<string> Acknowledge(GatewayPaymentResult result, string eventType, string orderId)
    {
        // Outcomes that a retry cannot fix are logged and acknowledged (2xx) so Razorpay stops redelivering.
        switch (result.Outcome)
        {
            case "AmountMismatch":
            case "RefundRequired":
            case "PaymentIdConflict":
                logger.LogError("Razorpay webhook {EventType} for order {OrderId} needs attention: {Outcome} (booking {BookingId}).", eventType, orderId, result.Outcome, result.BookingId);
                break;
            case "OrderNotFound":
                logger.LogWarning("Razorpay webhook {EventType} references unknown order {OrderId}.", eventType, orderId);
                break;
        }
        return ResponseFactory.Success(result.Outcome, "Webhook processed.");
    }

    // ------------------------------------------------------------------ helpers

    private bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);

    private bool IsValidCheckoutSignature(string orderId, string paymentId, string signature) =>
        IsValidSignature($"{orderId}|{paymentId}", KeySecret, signature);

    /// <summary>HMAC-SHA256 (hex) of <paramref name="payload"/> compared in constant time.</summary>
    internal static bool IsValidSignature(string payload, string secret, string signature)
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        byte[] provided;
        try { provided = Convert.FromHexString(signature.Trim()); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static long ToPaise(decimal rupees) => (long)Math.Round(rupees * 100m, MidpointRounding.AwayFromZero);

    private static JsonElement Entity(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Object
            && n.TryGetProperty("entity", out var e) ? e : default;

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private async Task<string> CreateGatewayOrderAsync(long amountPaise, BookingPaymentContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync("orders", new
            {
                amount = amountPaise,
                currency = Currency,
                receipt = context.BookingNo.Length > 40 ? context.BookingNo[..40] : context.BookingNo,
                notes = new { booking_id = context.BookingId.ToString(), booking_no = context.BookingNo }
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new RazorpayApiException($"Razorpay returned {(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var id = Str(doc.RootElement, "id");
            if (!IsValidId(id)) throw new RazorpayApiException("Razorpay response did not contain an order id.");
            return id;
        }
        catch (HttpRequestException ex) { throw new RazorpayApiException("Razorpay request failed.", ex); }
        catch (JsonException ex) { throw new RazorpayApiException("Razorpay returned an unreadable response.", ex); }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) { throw new RazorpayApiException("Razorpay request timed out.", ex); }
    }

    private sealed class RazorpayApiException(string message, Exception? inner = null) : Exception(message, inner);
}
