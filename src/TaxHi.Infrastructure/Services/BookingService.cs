using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
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

    public async Task<CommonResponse<PagedResult<UpcomingBookingDto>>> GetUpcomingBookingsAsync(
        string? phoneNumber,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return ResponseFactory.Failure<PagedResult<UpcomingBookingDto>>(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED");
        if (currentPage < 1)
            return ResponseFactory.Failure<PagedResult<UpcomingBookingDto>>(StatusCodes.Status400BadRequest, "CurrentPage must be greater than zero.", "INVALID_CURRENT_PAGE");
        if (pageSize is < 1 or > 100)
            return ResponseFactory.Failure<PagedResult<UpcomingBookingDto>>(StatusCodes.Status400BadRequest, "PageSize must be between 1 and 100.", "INVALID_PAGE_SIZE");

        try
        {
            var bookings = await repository.GetUpcomingBookingsAsync(MobileNumberHelper.NormalizeMobile(phoneNumber), currentPage, pageSize, cancellationToken);
            return ResponseFactory.Success(bookings, bookings.TotalItems == 0
                ? "No upcoming bookings found."
                : "Upcoming bookings retrieved successfully.");
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Database error while fetching upcoming bookings (SQL error {Number}).", ex.Number);
            return ResponseFactory.Failure<PagedResult<UpcomingBookingDto>>(StatusCodes.Status500InternalServerError, "Unable to fetch upcoming bookings right now. Please try again.", "UPCOMING_BOOKINGS_FETCH_FAILED");
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
