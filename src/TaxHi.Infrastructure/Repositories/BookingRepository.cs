using System.Data;
using System.Xml.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TaxHi.Application.Services;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingRepository(IDbConnection connection, IOptions<BookingOptions> options) : IBookingRepository
{
    private const int MaxDeadlockAttempts = 3;

    public async Task<SaveBookingPaymentResponse> SaveBookingPaymentAsync(
        SaveBookingPaymentRequest request,
        BookingAmounts amounts,
        CancellationToken cancellationToken = default)
    {
        var addOns = request.AddOns is { Count: > 0 }
            ? new XElement("AddOns", request.AddOns.Select(addOn => new XElement("AddOn",
                new XElement("AddOnId", addOn.AddOnId),
                new XElement("AddOnName", addOn.AddOnName),
                new XElement("PriceType", addOn.PriceType),
                new XElement("UnitPrice", addOn.UnitPrice),
                new XElement("CalculatedAmount", addOn.CalculatedAmount)  
            ))).ToString(SaveOptions.DisableFormatting)
            : null;

        var parameters = new DynamicParameters();
        parameters.Add("ClientRequestId", request.ClientRequestId, DbType.Guid);
        parameters.Add("Name", request.Name.Trim(), DbType.String);
        parameters.Add("PhoneNumber", request.PhoneNumber.Trim(), DbType.String);
        parameters.Add("Email", request.Email.Trim(), DbType.String);
        parameters.Add("AlternateEmail", request.AlternateEmail, DbType.String);
        parameters.Add("HasGST", request.HasGST, DbType.Boolean);
        parameters.Add("GSTNumber", request.HasGST ? request.GSTNumber?.Trim().ToUpperInvariant() : null, DbType.String);
        parameters.Add("GSTCompanyName", request.HasGST ? request.GSTCompanyName?.Trim() : null, DbType.String);
        parameters.Add("TripType", request.TripType.Trim(), DbType.String);
        parameters.Add("FromCityId", request.FromCityId, DbType.Int32);
        parameters.Add("ToCityId", request.ToCityId, DbType.Int32);
        parameters.Add("Route", request.Route, DbType.String);
        parameters.Add("PickupDateTime", request.PickupDateTime, DbType.DateTime2);
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
        parameters.Add("CouponCode", string.IsNullOrWhiteSpace(request.CouponCode) ? null : request.CouponCode.Trim(), DbType.String);
        parameters.Add("DiscountAmount", request.DiscountAmount, DbType.Decimal);
        parameters.Add("BaseFare", request.BaseFare, DbType.Decimal);
        parameters.Add("DriverAllowance", request.DriverAllowance, DbType.Decimal);
        parameters.Add("AddOnsTotalAmount", request.AddOnsTotalAmount, DbType.Decimal);
        parameters.Add("GSTAmount", request.GSTAmount, DbType.Decimal);
        parameters.Add("TotalFare", amounts.TotalFare, DbType.Decimal);
        parameters.Add("PayableNowAmount", amounts.PayableNowAmount, DbType.Decimal);
        parameters.Add("BalancePayableAmount", amounts.BalancePayableAmount, DbType.Decimal);
        parameters.Add("AddOns", addOns, DbType.Xml);
        parameters.Add("HoldMinutes", options.Value.HoldDurationMinutes, DbType.Int32);
        parameters.Add("LockTimeoutMs", options.Value.LockTimeoutMilliseconds, DbType.Int32);

        var command = new CommandDefinition(
            "USP_SaveBookingPayment",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        // Amounts come back from the database so an idempotent replay returns the ORIGINAL booking's figures.
        // A deadlock victim (1205) has been fully rolled back by SQL Server, and ClientRequestId makes the
        // procedure idempotent, so retrying the identical call is safe.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await connection.QuerySingleAsync<SaveBookingPaymentResponse>(command);
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < MaxDeadlockAttempts)
            {
                await Task.Delay(Random.Shared.Next(20, 80) * attempt, cancellationToken);
            }
        }
    }
}
