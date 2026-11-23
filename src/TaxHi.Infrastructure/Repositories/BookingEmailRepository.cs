using System.Data;
using Dapper;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingEmailRepository(IDbConnection connection) : IBookingEmailRepository
{
    public async Task<BookingConfirmationEmailDto?> GetConfirmationEmailDetailsAsync(
        long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        // TaxHiBEBookings.BookingId is INT; anything larger cannot exist.
        if (bookingId is <= 0 or > int.MaxValue) return null;

        var command = new CommandDefinition(
            "USP_GetBookingConfirmationEmailDetails",
            new { BookingId = (int)bookingId, UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BookingConfirmationEmailDto>(command);
    }

    public async Task SaveAuditLogAsync(
        int vendorId, long userId, string actionName, string details, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "SaveTransportVendorAuditLog",
            new { VendorId = vendorId, UserId = userId, ActionName = actionName, Details = details },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
