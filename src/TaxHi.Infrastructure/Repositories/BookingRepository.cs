using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Xml.Linq;
using TaxHi.Application.Models;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Common;
using TaxHi.Infrastructure.Models;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingRepository(IDbConnection connection, IConfiguration configuration) : IBookingRepository
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
            parameters.Add("UserId", (long)userId, DbType.Int64);

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
                DbType.DateTime2);

            parameters.Add(
                "DropDate",
                request.DropDate?.ToDateTime(TimeOnly.MinValue),
                DbType.Date);

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
            parameters.Add("HoldMinutes", BookingSettings.HoldDurationMinutes(configuration), DbType.Int32);
            parameters.Add("LockTimeoutMs", BookingSettings.LockTimeoutMilliseconds(configuration), DbType.Int32);

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
                    ex.Number == 50007 &&
                    attempt < MaxBookingNoAttempts)
                {
                    parameters.Add(
                        "TripCode",
                        GetTripCode(),
                        DbType.AnsiString,
                        size: 50);
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
           

            throw;
        }
        catch (Exception ex)
        {
           
            throw;
        }
    }



    public async Task<(VendorConfig? Config, IReadOnlyList<VendorItem> Items)> GetVendorConfigurationAsync(
        int? vendorId, int? vehicleBrandId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("VendorId", vendorId, DbType.Int32);
        parameters.Add("VehicleBrandId", vehicleBrandId, DbType.Int32);

        var command = new CommandDefinition(
            "USP_GetVendorConfiguration",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var config = await resultSets.ReadFirstOrDefaultAsync<VendorConfig>();
        var items = (await resultSets.ReadAsync<VendorItem>()).AsList();
        return (config, items);
    }

    public async Task<BookingListResponseDto> GetBookingsAsync(
    int userId,
    string mode,
    int currentPage,
    int pageSize,
    CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId, DbType.Int32);
        parameters.Add("Mode", mode, DbType.AnsiString, size: 20);
        parameters.Add("CurrentPage", currentPage, DbType.Int32);
        parameters.Add("PageSize", pageSize, DbType.Int32);

        // PickupDateTime is stored and validated against server-local time.
        parameters.Add("CurrentDateTime", DateTime.Now, DbType.DateTime2);

        var command = new CommandDefinition(
            "USP_GetUserBookings",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);

        var rows = (await resultSets.ReadAsync<BookingDto>()).AsList();
        var meta = await resultSets.ReadSingleOrDefaultAsync<BookingListMetadata>();
        var counts = await resultSets.ReadSingleOrDefaultAsync<BookingTabCounts>();

        return new BookingListResponseDto
        {
            Mode = meta?.Mode ?? mode,
            CurrentPage = meta?.CurrentPage ?? currentPage,
            PageSize = meta?.PageSize ?? pageSize,
            TotalRecords = meta?.TotalRecords ?? rows.Count,
            TotalPages = meta?.TotalPages ?? (rows.Count == 0 ? 0 : 1),
            UpcomingCount = counts?.UpcomingCount ?? 0,
            CurrentCount = counts?.CurrentCount ?? 0,
            HistoryCount = counts?.HistoryCount ?? 0,
            Items = rows
        };
    }

    // ---------------------------------------------------------------- Razorpay payment state (database/RazorpayPayment.sql)

    public async Task<BookingPaymentContext?> GetPaymentContextAsync(
        long? bookingId, string? gatewayOrderId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BookingId", bookingId is { } id ? (int?)id : null, DbType.Int32);
        parameters.Add("GatewayOrderId", gatewayOrderId, DbType.AnsiString, size: 64);

        return await connection.QuerySingleOrDefaultAsync<BookingPaymentContext>(new CommandDefinition(
            "USP_GetBookingPaymentContext", parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<string> SetGatewayOrderAsync(
        long bookingPaymentId, string gatewayOrderId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BookingPaymentId", bookingPaymentId, DbType.Int64);
        parameters.Add("GatewayOrderId", gatewayOrderId, DbType.AnsiString, size: 64);

        return await connection.QuerySingleAsync<string>(new CommandDefinition(
            "USP_SetBookingPaymentGatewayOrder", parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public Task<GatewayPaymentResult> ConfirmGatewayPaymentAsync(
        string gatewayOrderId, string gatewayPaymentId, long? amountPaise, string? paymentMethod,
        string? eventId, string? eventType, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("GatewayOrderId", gatewayOrderId, DbType.AnsiString, size: 64);
        parameters.Add("GatewayPaymentId", gatewayPaymentId, DbType.AnsiString, size: 64);
        parameters.Add("AmountPaise", amountPaise, DbType.Int64);
        parameters.Add("PaymentMethod", paymentMethod, DbType.String, size: 50);
        parameters.Add("EventId", eventId, DbType.AnsiString, size: 100);
        parameters.Add("EventType", eventType, DbType.AnsiString, size: 60);
        parameters.Add("HoldMinutes", BookingSettings.HoldDurationMinutes(configuration), DbType.Int32);
        parameters.Add("LockTimeoutMs", BookingSettings.LockTimeoutMilliseconds(configuration), DbType.Int32);
        return RunGatewayProcedureAsync("USP_ConfirmBookingGatewayPayment", parameters, cancellationToken);
    }

    public Task<GatewayPaymentResult> FailGatewayPaymentAsync(
        string gatewayOrderId, string? gatewayPaymentId, string? reason,
        string? eventId, string? eventType, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("GatewayOrderId", gatewayOrderId, DbType.AnsiString, size: 64);
        parameters.Add("GatewayPaymentId", gatewayPaymentId, DbType.AnsiString, size: 64);
        parameters.Add("Reason", reason is { Length: > 500 } ? reason[..500] : reason, DbType.String, size: 500);
        parameters.Add("EventId", eventId, DbType.AnsiString, size: 100);
        parameters.Add("EventType", eventType, DbType.AnsiString, size: 60);
        return RunGatewayProcedureAsync("USP_FailBookingGatewayPayment", parameters, cancellationToken);
    }

    private async Task<GatewayPaymentResult> RunGatewayProcedureAsync(
        string procedure, DynamicParameters parameters, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure,
            commandTimeout: 120, cancellationToken: cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await connection.QuerySingleAsync<GatewayPaymentResult>(command);
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < MaxDeadlockAttempts)
            {
                await Task.Delay(Random.Shared.Next(20, 80) * attempt, cancellationToken);
            }
        }
    }

    // ------------------------------------------------------------------ cancellation

    public async Task<BookingCancellationContext?> GetCancellationContextAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        // TaxHiBEBookings.BookingId is INT; anything larger cannot exist.
        if (bookingId is <= 0 or > int.MaxValue) return null;

        return await connection.QuerySingleOrDefaultAsync<BookingCancellationContext>(new CommandDefinition(
            "USP_GetBookingCancellationContext",
            new { BookingId = (int)bookingId, UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<BookingCancellationResult> CancelBookingAsync(
        long bookingId, long userId, long? tripId, string reason, string? remarks,
        decimal refundAmount, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BookingId", (int)bookingId, DbType.Int32);
        parameters.Add("UserId", userId, DbType.Int64);
        parameters.Add("Reason", reason, DbType.String);
        parameters.Add("Remarks", remarks, DbType.String);

        return await connection.QuerySingleAsync<BookingCancellationResult>(new CommandDefinition(
            "USP_CancelBooking",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
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
