using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxHi.Application.Enums;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;
using TaxHi.Infrastructure.Helpers;
using TaxHi.Infrastructure.Repositories;

namespace TaxHi.Infrastructure.Services;

public sealed partial class BookingService(
    IBookingRepository repository,
    IBookingInvoiceService invoiceService,
    IBookingRouteRepository routeRepository,
    IBookingEmailRepository emailRepository,
    IAuthenticationRepository authenticationRepository,
    IOtpNotifier otpNotifier,
    IOptions<AuthenticationOptions> authenticationOptions,
    IBookingConfirmationEmailService emailService,
    IConfiguration configuration,
    ILogger<BookingService> logger) : IBookingService
{
    private const decimal PartPayPercent = 25m;
    private const decimal Tolerance = 0.01m;

    [GeneratedRegex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$")]
    private static partial Regex GstinPattern();

    public async Task<CommonResponse<SaveBookingPaymentResponse>> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        var error = Validate(request, out var amounts);
        if (error is not null)
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status400BadRequest, error.Value.Message, error.Value.Code);

        try
        {
            var saved = await repository.SaveBookingPaymentAsync(request, userId, amounts!, BookingRepository.GenerateBookingNo(), BookingRepository.GetTripCode(), cancellationToken);

            // The booking is committed at this point. Invoice generation is best-effort and never throws,
            // so a PDF problem cannot turn a saved booking into a failed response.
            var invoice = await invoiceService.EnsureInvoiceAsync(saved.BookingId, userId, saved.BookingNo, cancellationToken: cancellationToken);
            saved.InvoiceGenerated = invoice.InvoiceGenerated;
            saved.InvoiceUrl = invoice.InvoiceUrl;

            // Optional Google route details; best-effort for the same reason as the invoice.
            if (request.RouteDetails is not null && !saved.IsDuplicateRequest)
            {
                try
                {
                    await routeRepository.SaveRouteDetailsAsync(saved.BookingId, request.RouteDetails, cancellationToken);
                }
                catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Could not save route details for booking {BookingId} (SQL error {Number}).", saved.BookingId, ex.Number);
                }
            }

            return ResponseFactory.Success(saved, saved.IsDuplicateRequest
                ? "This booking request was already processed. Returning the existing booking."
                : "Booking saved successfully. Payment is pending.");
        }
        catch (SqlException ex) when (ex.Number == 50003)
        {
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status400BadRequest,
                "This vehicle has already been reserved for the selected date and time. Please select another vehicle or date.",
                "VEHICLE_ALREADY_RESERVED");
        }
        catch (SqlException ex) when (ex.Number is 50002 or 1205) // vehicle lock timeout / deadlock victim
        {
            logger.LogWarning(ex, "Vehicle booking lock contention (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status409Conflict,
                "The vehicle is being booked by another customer right now. Please try again in a moment.",
                "BOOKING_LOCK_TIMEOUT");
        }
        catch (SqlException ex) when (ex.Number is 50004 or 50006)
        {
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status400BadRequest, ex.Message, "INVALID_BOOKING_DATA");
        }
        catch (SqlException ex) when (ex.Number is 547) // FK / CHECK violation
        {
            logger.LogWarning(ex, "Constraint violation while saving booking.");
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status400BadRequest, "The booking data violates a database constraint.", "BOOKING_CONSTRAINT_VIOLATION");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while saving booking (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<SaveBookingPaymentResponse>(StatusCodes.Status500InternalServerError, "Unable to save the booking right now. Please try again.", "BOOKING_SAVE_FAILED");
        }
    }

    private static readonly string[] BookingModes = ["Upcoming", "Current", "History"];

    public async Task<CommonResponse<BookingListResponseDto>> GetBookingsAsync(
        int userId,
        string? mode,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return ResponseFactory.Failure<BookingListResponseDto>(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED");

        var normalizedMode = BookingModes.FirstOrDefault(m => string.Equals(m, mode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedMode is null)
            return ResponseFactory.Failure<BookingListResponseDto>(StatusCodes.Status400BadRequest, "Mode must be Upcoming, Current or History.", "INVALID_MODE");
        if (currentPage < 1)
            return ResponseFactory.Failure<BookingListResponseDto>(StatusCodes.Status400BadRequest, "CurrentPage must be greater than zero.", "INVALID_CURRENT_PAGE");
        if (pageSize is < 1 or > 100)
            return ResponseFactory.Failure<BookingListResponseDto>(StatusCodes.Status400BadRequest, "PageSize must be between 1 and 100.", "INVALID_PAGE_SIZE");

        try
        {
            var bookings = await repository.GetBookingsAsync(userId, normalizedMode, currentPage, pageSize, cancellationToken);
            return ResponseFactory.Success(bookings, bookings.TotalRecords == 0
                ? $"No {normalizedMode.ToLowerInvariant()} bookings found."
                : $"{normalizedMode} bookings retrieved successfully.");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while fetching bookings (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<BookingListResponseDto>(StatusCodes.Status500InternalServerError, "Unable to fetch bookings right now. Please try again.", "BOOKINGS_FETCH_FAILED");
        }
    }

    // ------------------------------------------------------------------ cancellation

    private const string RefundPending = "Pending";

    private static string CancellationOtpPurpose(long bookingId) => $"BookingCancellation:{bookingId}";

    public async Task<CommonResponse<object>> SendCancellationOtpAsync(
        SendCancellationOtpRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return ResponseFactory.Failure<object>(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED");
        if (request is null || request.BookingId is <= 0 or > int.MaxValue)
            return ResponseFactory.Failure<object>(StatusCodes.Status400BadRequest, "A valid BookingId is required.", "INVALID_BOOKING_ID");

        try
        {
            var context = await repository.GetCancellationContextAsync(request.BookingId, userId, cancellationToken);
            if (context is null)
                return ResponseFactory.Failure<object>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");
            var statusError = CancellableError(context);
            if (statusError is not null)
                return ResponseFactory.Failure<object>(statusError.StatusCode, statusError.Message, statusError.ErrorCode ?? "BOOKING_NOT_CANCELLABLE");

            // The OTP goes to the account holder's own mobile number, not to a number typed into the booking.
            var user = await authenticationRepository.GetUserByIdAsync(userId, cancellationToken);
            if (user is null || !MobileNumberHelper.IsValidMobile(user.MobileNumber))
                return ResponseFactory.Failure<object>(StatusCodes.Status409Conflict, "No valid mobile number is registered for this account.", "MOBILE_NOT_AVAILABLE");

            var settings = authenticationOptions.Value;
            var mobile = MobileNumberHelper.NormalizeMobile(user.MobileNumber);
            var purpose = CancellationOtpPurpose(context.BookingId);
            var otp = OtpService.CreateNumericCode(6);
            var created = await authenticationRepository.CreateOtpAsync(mobile, purpose, TokenHelper.Hash(otp, settings.JwtSigningKey),
                DateTime.Now.AddMinutes(settings.OtpExpiryMinutes), settings.OtpMaxAttempts, settings.OtpResendCooldownSeconds, cancellationToken);
            if (!created.Accepted)
                return ResponseFactory.Failure<object>(StatusCodes.Status429TooManyRequests, "OTP resend is temporarily unavailable.", "OTP_RATE_LIMITED", created.RetryAfterSeconds);

            await otpNotifier.SendAsync(mobile, purpose, otp, cancellationToken);
            return ResponseFactory.Success<object>(new { created.Expires }, "Cancellation OTP sent successfully.");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while sending the cancellation OTP for booking {BookingId} (SQL error {Number}).", request.BookingId, ex.Number);
            return ResponseFactory.Failure<object>(StatusCodes.Status500InternalServerError, "Unable to send the OTP right now. Please try again.", "OTP_SEND_FAILED");
        }
    }

    public async Task<CommonResponse<CancelBookingResponse>> CancelBookingAsync(
        CancelBookingRequest request, int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED");
        if (request is null || request.BookingId is <= 0 or > int.MaxValue)
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "A valid BookingId is required.", "INVALID_BOOKING_ID");
        if (request.TripId is > int.MaxValue or < 0)
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "TripId is not valid.", "INVALID_TRIP_ID");

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 200)
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "A cancellation reason is required (max 200 characters).", "INVALID_REASON");
        var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        if (remarks is { Length: > 500 })
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "Remarks can be at most 500 characters.", "INVALID_REMARKS");
        var otp = request.Otp?.Trim() ?? string.Empty;
        if (otp.Length != 6 || !otp.All(char.IsDigit))
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "A valid 6-digit OTP is required to cancel a booking.", "OTP_REQUIRED");
        long? tripId = request.TripId is > 0 ? request.TripId : null; // 0 means "not supplied"

        BookingCancellationContext? context;
        BookingCancellationResult result;
        try
        {
            context = await repository.GetCancellationContextAsync(request.BookingId, userId, cancellationToken);
            // Same answer for "not yours" and "missing" so booking ids cannot be probed.
            if (context is null)
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");
            if (tripId is not null && context.TripId != tripId)
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "The trip does not belong to this booking.", "TRIP_MISMATCH");

            var statusError = CancellableError(context);
            if (statusError is not null)
                return statusError;

            // OTP is checked last, after the cheap rejections, so a wrong booking never burns an attempt. It is mandatory.
            var user = await authenticationRepository.GetUserByIdAsync(userId, cancellationToken);
            if (user is null || !MobileNumberHelper.IsValidMobile(user.MobileNumber))
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status409Conflict, "No valid mobile number is registered for this account.", "MOBILE_NOT_AVAILABLE");
            var verification = await authenticationRepository.VerifyOtpAsync(
                MobileNumberHelper.NormalizeMobile(user.MobileNumber), CancellationOtpPurpose(context.BookingId),
                TokenHelper.Hash(otp, authenticationOptions.Value.JwtSigningKey), cancellationToken);
            if (!verification.Success)
                return ResponseFactory.Failure<CancelBookingResponse>(
                    verification.FailureCode == "OTP_MAX_ATTEMPTS" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized,
                    "The OTP is invalid or has expired.", string.IsNullOrEmpty(verification.FailureCode) ? "OTP_INVALID" : verification.FailureCode);

            var refund = CalculateRefund(context, DateTime.Now);
            result = await repository.CancelBookingAsync(context.BookingId, userId, tripId, reason, remarks, refund, cancellationToken);
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while cancelling booking {BookingId} (SQL error {Number}).", request.BookingId, ex.Number);
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status500InternalServerError, "Unable to cancel the booking right now. Please try again.", "BOOKING_CANCEL_FAILED");
        }

        // The stored procedure re-checks under a lock, so a concurrent cancel/payment can still lose the race here.
        switch (result.Outcome)
        {
            case "Cancelled":
                break;
            case "NotFound":
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status404NotFound, "Booking not found.", "BOOKING_NOT_FOUND");
            case "TripMismatch":
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status400BadRequest, "The trip does not belong to this booking.", "TRIP_MISMATCH");
            default:
                return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status409Conflict,
                    $"This booking is {(result.PreviousStatus ?? "no longer active").ToLowerInvariant()} and cannot be cancelled.", "BOOKING_NOT_CANCELLABLE");
        }

        // From here the cancellation is committed. Nothing below may fail it, and a client disconnect must not
        // skip the refund or the audit, so none of it observes the request's cancellation token.
        var cancelledDate = DateTime.SpecifyKind(result.CancelledDateUtc ?? DateTime.UtcNow, DateTimeKind.Utc).ToLocalTime();
        // Refunds are paid manually by the accounts team; the cancellation email tells them what is due.
        var refundStatus = result.RefundStatus;

        await TryAuditAsync(context.VendorId, userId, "BookingCancelled",
            $"Booking {context.BookingNo}: cancelled by user {userId}. Reason: {reason}. Remarks: {remarks ?? "-"}. Refund due: {result.RefundAmount:0.00} ({refundStatus}, manual).");

        try
        {
            await emailService.SendBookingCancellationAsync(context.BookingId, userId,
                new BookingCancellationEmailInfo(reason, remarks, cancelledDate, result.RefundAmount, refundStatus,
                    context.AmountPaid, context.PaymentGatewayPaymentId));
        }
        catch (Exception ex)
        {
            // The email service already swallows its own failures; this guards against anything else (e.g. DI/config).
            logger.LogError(ex, "Cancellation email failed for booking {BookingId}.", context.BookingId);
        }

        return ResponseFactory.Success(new CancelBookingResponse
        {
            BookingId = context.BookingId,
            BookingNo = context.BookingNo,
            BookingStatus = "Cancelled",
            CancelledDate = cancelledDate,
            RefundAmount = result.RefundAmount,
            RefundStatus = refundStatus,
            RefundMessage = refundStatus switch
            {
                RefundPending => "Your refund has been recorded. Our accounts team will process it manually.",
                _ => "No refund applies to this cancellation."
            }
        }, "Booking cancelled successfully.");
    }

    private static CommonResponse<CancelBookingResponse>? CancellableError(BookingCancellationContext c)
    {
        if (c.BookingStatus is not ("Pending" or "Confirmed"))
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status409Conflict,
                c.BookingStatus == "Cancelled" ? "This booking is already cancelled." : $"A {c.BookingStatus.ToLowerInvariant()} booking cannot be cancelled.",
                c.BookingStatus == "Cancelled" ? "ALREADY_CANCELLED" : "BOOKING_NOT_CANCELLABLE");
        if (c.PickupDateTime is { } pickup && pickup <= DateTime.Now)
            return ResponseFactory.Failure<CancelBookingResponse>(StatusCodes.Status409Conflict,
                "This trip has already started or completed and cannot be cancelled.", "BOOKING_NOT_CANCELLABLE");
        return null;
    }

    /// <summary>Full refund well before pickup, a percentage closer to it, nothing inside the last window. Unpaid bookings refund nothing.</summary>
    private decimal CalculateRefund(BookingCancellationContext c, DateTime now)
    {
        if (c.AmountPaid <= 0 || c.PickupDateTime is not { } pickup) return 0m;

        var hoursToPickup = (pickup - now).TotalHours;
        if (hoursToPickup >= BookingSettings.FullRefundHoursBeforePickup(configuration)) return Round(c.AmountPaid);
        if (hoursToPickup >= BookingSettings.PartialRefundHoursBeforePickup(configuration)) return Round(c.AmountPaid * Math.Clamp(BookingSettings.PartialRefundPercent(configuration), 0m, 100m) / 100m);
        return 0m;
    }

    /// <summary>Audit writing is best effort: a logging problem must not surface.</summary>
    private async Task TryAuditAsync(int vendorId, long userId, string action, string text)
    {
        try
        {
            await emailRepository.SaveAuditLogAsync(vendorId, userId, action, text.Length <= 2000 ? text : text[..2000], CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the booking audit log ({Action}).", action);
        }
    }

    private static (string Message, string Code)? Validate(SaveBookingPaymentRequest r, out BookingAmounts? amounts)
    {
        amounts = null;

        if (r.ClientRequestId == Guid.Empty)
            return ("ClientRequestId (GUID) is required.", "INVALID_CLIENT_REQUEST_ID");
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200)
            return ("Name is required (max 200 characters).", "INVALID_NAME");
        if (!MobileNumberHelper.IsValidMobile(r.PhoneNumber))
            return ("A valid phone number is required.", "INVALID_PHONE");
        if (!IsValidEmail(r.Email))
            return ("A valid email is required.", "INVALID_EMAIL");
        if (!string.IsNullOrWhiteSpace(r.AlternateEmail) && !IsValidEmail(r.AlternateEmail))
            return ("Alternate email is not valid.", "INVALID_ALTERNATE_EMAIL");

        if (r.HasGST)
        {
            if (string.IsNullOrWhiteSpace(r.GSTNumber) || !GstinPattern().IsMatch(r.GSTNumber.Trim().ToUpperInvariant()))
                return ("A valid 15-character GST number is required.", "INVALID_GST_NUMBER");
            if (string.IsNullOrWhiteSpace(r.GSTCompanyName))
                return ("GST company name is required.", "INVALID_GST_COMPANY");
        }

        if (string.IsNullOrWhiteSpace(r.TripType))
            return ("TripType is required.", "INVALID_TRIP_TYPE");
        if (r.FromCityId <= 0 || r.ToCityId <= 0)
            return ("FromCityId and ToCityId are required.", "INVALID_CITY");
        if (r.PickupDateTime <= DateTime.Now)
            return ("PickupDateTime must be in the future.", "INVALID_PICKUP_DATETIME");
        if (r.DropDate is { } drop && drop < DateOnly.FromDateTime(r.PickupDateTime))
            return ("DropDate cannot be before the pickup date.", "INVALID_DROP_DATE");
        if (string.IsNullOrWhiteSpace(r.PickupLocation) || string.IsNullOrWhiteSpace(r.DropLocation))
            return ("PickupLocation and DropLocation are required.", "INVALID_LOCATION");
        if (r.RouteDetails is { } route && !IsValidRoute(route))
            return ("RouteDetails are invalid.", "INVALID_ROUTE_DETAILS");

        if (r.VendorId <= 0 || r.VehicleId <= 0 || r.VehicleDetailsId <= 0 || r.VehicleCategoryId <= 0 || r.VehicleBrandId <= 0)
            return ("Vendor and vehicle identifiers are required.", "INVALID_VEHICLE");
        if (string.IsNullOrWhiteSpace(r.VehicleName))
            return ("VehicleName is required.", "INVALID_VEHICLE_NAME");

        if (!Enum.IsDefined(typeof(PaymentOption), r.PaymentOption))
            return ("PaymentOption must be PartPay or FullPay.", "INVALID_PAYMENT_OPTION");

        if (r.DiscountAmount < 0 || r.BaseFare <= 0 || r.DriverAllowance < 0 || r.AddOnsTotalAmount < 0 || r.GSTAmount < 0)
            return ("Fare amounts are invalid.", "INVALID_FARE");

        var addOns = r.AddOns ?? [];
        foreach (var addOn in addOns)
        {
            if (addOn.AddOnId <= 0 || string.IsNullOrWhiteSpace(addOn.AddOnName) || string.IsNullOrWhiteSpace(addOn.PriceType) || addOn.UnitPrice < 0 || addOn.CalculatedAmount < 0)
                return ("One or more selected add-ons are invalid.", "INVALID_ADDON");
        }
        if (addOns.Select(a => a.AddOnId).Distinct().Count() != addOns.Count)
            return ("Duplicate add-ons are not allowed.", "DUPLICATE_ADDON");

        var addOnsSum = Round(addOns.Sum(a => a.CalculatedAmount));
        if (Math.Abs(addOnsSum - Round(r.AddOnsTotalAmount)) > Tolerance)
            return ("AddOnsTotalAmount does not match the selected add-ons.", "ADDONS_TOTAL_MISMATCH");

        var total = Round(r.BaseFare + r.DriverAllowance + addOnsSum + r.GSTAmount - r.DiscountAmount);
        if (total <= 0)
            return ("TotalFare must be greater than zero.", "INVALID_TOTAL_FARE");
        if (Math.Abs(total - Round(r.TotalFare)) > Tolerance)
            return ("TotalFare does not match the fare breakup.", "TOTAL_FARE_MISMATCH");

        var payNow = r.PaymentOption == PaymentOption.PartPay.ToString() ? Round(total * PartPayPercent / 100m) : total;
        amounts = new BookingAmounts(total, payNow, Round(total - payNow));
        return null;
    }

    private static bool IsValidRoute(TaxHi.Application.Models.Maps.BookingRouteDetailsDto r) =>
        IsValidCoordinate(r.PickupLatitude, r.PickupLongitude)
        && IsValidCoordinate(r.DropLatitude, r.DropLongitude)
        && r.GoogleDistanceKm is >= 0 and <= 99999
        && r.GoogleDurationMinutes is >= 0 and <= 100000
        && (r.PickupPlaceId?.Length ?? 0) <= 300
        && (r.DropPlaceId?.Length ?? 0) <= 300
        && (r.EncodedPolyline?.Length ?? 0) <= 200000;

    private static bool IsValidCoordinate(double lat, double lng) =>
        double.IsFinite(lat) && double.IsFinite(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320) return false;
        return MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim();
    }
}
