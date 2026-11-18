using System.Data;
using Dapper;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingInvoiceRepository(IDbConnection connection) : IBookingInvoiceRepository
{
    // TaxHiBEBookings.BookingId is INT; anything larger cannot exist.
    private static bool FitsInt(long id) => id is > 0 and <= int.MaxValue;

    public async Task<BookingInvoiceDto?> GetInvoiceDataAsync(long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        if (!FitsInt(bookingId)) return null;

        var command = new CommandDefinition(
            "USP_GetBookingInvoiceData",
            new { BookingId = (int)bookingId, UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var results = await connection.QueryMultipleAsync(command);
        var invoice = await results.ReadSingleOrDefaultAsync<BookingInvoiceDto>();
        if (invoice is null) return null;

        invoice.AddOns = (await results.ReadAsync<BookingInvoiceAddOnDto>()).AsList();
        invoice.Payments = (await results.ReadAsync<BookingInvoicePaymentDto>()).AsList();
        return invoice;
    }

    public async Task<BookingInvoiceFileInfo?> GetInvoiceFileInfoAsync(long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        if (!FitsInt(bookingId)) return null;

        var command = new CommandDefinition(
            "USP_GetBookingInvoiceFile",
            new { BookingId = (int)bookingId, UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BookingInvoiceFileInfo>(command);
    }

    public async Task SaveInvoicePathAsync(long bookingId, string invoiceFilePath, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "USP_SaveBookingInvoicePath",
            new { BookingId = (int)bookingId, InvoiceFilePath = invoiceFilePath },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
