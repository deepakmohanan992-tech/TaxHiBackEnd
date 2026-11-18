using System.Data;
using System.Xml.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TaxHi.Application.Services;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Models;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingRepository(IDbConnection connection, IOptions<BookingOptions> options) : IBookingRepository
{
    private const int MaxDeadlockAttempts = 3;
    private const int MaxBookingNoAttempts = 5;


    public async Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,
        int userId,
        BookingAmounts amounts,
        string bookingNo,
        string tripCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var addOns = request.AddOns is { Count: > 0 }
                ? new XElement("AddOns", request.AddOns.Select(addOn =>
                    new XElement("AddOn",
                        new XElement("AddOnId", addOn.AddOnId),
                        new XElement("AddOnName", addOn.AddOnName),
                        new XElement("PriceType", addOn.PriceType),
                        new XElement("UnitPrice", addOn.UnitPrice),
                        new XElement("CalculatedAmount", addOn.CalculatedAmount)
                    ))).ToString(SaveOptions.DisableFormatting)
                : null;

            var parameters = new DynamicParameters();

            parameters.Add("ClientRequestId", request.ClientRequestId, DbType.Guid);

            // BIGINT in SQL Server
            parameters.Add("UserId", (long)userId, DbType.Int32);

            parameters.Add("Name", request.Name.Trim(), DbType.String);
            parameters.Add("PhoneNumber", request.PhoneNumber.Trim(), DbType.String);
            parameters.Add("Email", request.Email.Trim(), DbType.String);
            parameters.Add("AlternateEmail", request.AlternateEmail, DbType.String);
            parameters.Add("HasGST", request.HasGST, DbType.Boolean);

            parameters.Add(
                "GSTNumber",
                request.HasGST ? request.GSTNumber?.Trim().ToUpperInvariant() : null,
                DbType.String);

            parameters.Add(
                "GSTCompanyName",
                request.HasGST ? request.GSTCompanyName?.Trim() : null,
                DbType.String);

            parameters.Add("TripType", request.TripType.Trim(), DbType.String);
            parameters.Add("FromCityId", request.FromCityId, DbType.Int32);
            parameters.Add("ToCityId", request.ToCityId, DbType.Int32);
            parameters.Add("Route", request.Route, DbType.String);
            parameters.Add("PickupDateTime", request.PickupDateTime, DbType.DateTime2);
            parameters.Add(
    "ReturnDateTime",
    request.ReturnDateTime ?? request.PickupDateTime,
    DbType.DateTime2
);
            parameters.Add("DropDate", request.DropDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
            parameters.Add("PickupLocation", request.PickupLocation.Trim(), DbType.String);
            parameters.Add("DropLocation", request.DropLocation.Trim(), DbType.String);
            parameters.Add("VendorId", request.VendorId, DbType.Int32);
            parameters.Add("VehicleId", request.VehicleId, DbType.Int32);
            parameters.Add("VehicleDetailsId", request.VehicleDetailsId, DbType.Int32);
            parameters.Add("VehicleCategoryId", request.VehicleCategoryId, DbType.Int32);
            parameters.Add("VehicleBrandId", request.VehicleBrandId, DbType.Int32);
            parameters.Add("VehicleName", request.VehicleName.Trim(), DbType.String);
            parameters.Add("FuelType", request.FuelType, DbType.String);
            parameters.Add("PaymentOption", request.PaymentOption, DbType.String);

            parameters.Add(
                "CouponCode",
                string.IsNullOrWhiteSpace(request.CouponCode)
                    ? null
                    : request.CouponCode.Trim(),
                DbType.String);

            parameters.Add("DiscountAmount", request.DiscountAmount, DbType.Decimal);
            parameters.Add("BaseFare", request.BaseFare, DbType.Decimal);
            parameters.Add("DriverAllowance", request.DriverAllowance, DbType.Decimal);
            parameters.Add("AddOnsTotalAmount", request.AddOnsTotalAmount, DbType.Decimal);
            parameters.Add("GSTAmount", request.GSTAmount, DbType.Decimal);
            parameters.Add("TotalFare", amounts.TotalFare, DbType.Decimal);
            parameters.Add("PayableNowAmount", amounts.PayableNowAmount, DbType.Decimal);
            parameters.Add("BalancePayableAmount", amounts.BalancePayableAmount, DbType.Decimal);
            parameters.Add("BookingNo", bookingNo, DbType.String);
            parameters.Add("TripCode", tripCode, DbType.AnsiString, size: 50);
            parameters.Add("AddOns", addOns, DbType.Xml);
            parameters.Add("HoldMinutes", options.Value.HoldDurationMinutes, DbType.Int32);
            parameters.Add("LockTimeoutMs", options.Value.LockTimeoutMilliseconds, DbType.Int32);

            var command = new CommandDefinition(
                "USP_SaveBookingPayment",
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120,
                cancellationToken: cancellationToken);

            // Retry handling
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await connection.QuerySingleAsync<SaveBookingPaymentResponse>(
                        command);
                }
                catch (SqlException ex) when (
                    ex.Number == 1205 &&
                    attempt < MaxDeadlockAttempts)
                {
                    await Task.Delay(
                        Random.Shared.Next(20, 80) * attempt,
                        cancellationToken);
                }
                catch (SqlException ex) when (
                    ex.Number == 50007 && // TripCode already exists; nothing was saved, so try a fresh code
                    attempt < MaxBookingNoAttempts)
                {
                    parameters.Add("TripCode", GetTripCode(), DbType.AnsiString, size: 10);
                }
                catch (SqlException ex) when (
                    (ex.Number is 2601 or 2627) &&
                    ex.Message.Contains(
                        "BookingNo",
                        StringComparison.OrdinalIgnoreCase) &&
                    attempt < MaxBookingNoAttempts)
                {
                    parameters.Add(
                        "BookingNo",
                        GenerateBookingNo(),
                        DbType.String);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            // Database-related exception
            // Add application logging here if required.
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected exception
            // Add application logging here if required.
            throw;
        }
    }


    public async Task<PagedResult<UpcomingBookingDto>> GetUpcomingBookingsAsync(
        string phoneNumber,
        int currentPage,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PhoneNumber", phoneNumber, DbType.AnsiString, size: 20); // Bookings.PhoneNumber is VARCHAR(20)
        parameters.Add("CurrentPage", currentPage, DbType.Int32);
        parameters.Add("PageSize", pageSize, DbType.Int32);
        // PickupDateTime is stored and validated against server-local time (see BookingService.Validate).
        parameters.Add("CurrentDateTime", DateTime.Now, DbType.DateTime2);

        var command = new CommandDefinition(
            "USP_GetUpcomingBookings",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = (await resultSets.ReadAsync<UpcomingBookingDto>()).AsList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();

        return new PagedResult<UpcomingBookingDto>
        {
            Items = rows,
            CurrentPage = pagination?.CurrentPage ?? currentPage,
            PageSize = pagination?.PageSize ?? pageSize,
            TotalItems = pagination?.TotalRecords ?? rows.Count,
            TotalPages = pagination?.TotalPages ?? (rows.Count == 0 ? 0 : 1)
        };
    }

    private const string TripCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int TripCodeLength = 10;

    /// <summary>Random 10-character uppercase alphanumeric TripCode (cryptographically random, unbiased).</summary>
    public static string GetTripCode() =>
        string.Create(TripCodeLength, 0, static (span, _) =>
            System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(TripCodeAlphabet, span));

    // Format: M{MM}{yy}-{8 random digits}, e.g. M1126-13334779
    public static string GenerateBookingNo() =>
        $"T{DateTime.Now:MM}{DateTime.Now:yy}-{Random.Shared.Next(10000000, 99999999)}";
}
