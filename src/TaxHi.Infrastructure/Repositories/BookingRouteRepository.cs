using System.Data;
using Dapper;
using TaxHi.Application.Models.Maps;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class BookingRouteRepository(IDbConnection connection) : IBookingRouteRepository
{
    public async Task SaveRouteDetailsAsync(long bookingId, BookingRouteDetailsDto route, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BookingId", (int)bookingId, DbType.Int32);
        parameters.Add("PickupPlaceId", route.PickupPlaceId?.Trim(), DbType.String);
        parameters.Add("PickupLatitude", (decimal)route.PickupLatitude, DbType.Decimal);
        parameters.Add("PickupLongitude", (decimal)route.PickupLongitude, DbType.Decimal);
        parameters.Add("DropPlaceId", route.DropPlaceId?.Trim(), DbType.String);
        parameters.Add("DropLatitude", (decimal)route.DropLatitude, DbType.Decimal);
        parameters.Add("DropLongitude", (decimal)route.DropLongitude, DbType.Decimal);
        parameters.Add("GoogleDistanceKm", route.GoogleDistanceKm, DbType.Decimal);
        parameters.Add("GoogleDurationMinutes", route.GoogleDurationMinutes, DbType.Int32);
        parameters.Add("EncodedPolyline", route.EncodedPolyline, DbType.String);

        await connection.ExecuteAsync(new CommandDefinition(
            "USP_SaveBookingRouteDetails",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }
}
