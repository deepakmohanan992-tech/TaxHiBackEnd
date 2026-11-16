using System.Data;
using Dapper;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class LookupRepository(IDbConnection connection) : ILookupRepository
{
    public async Task<IReadOnlyList<SelectedItems>> GetVehicleTypesAsync(
        int? vendorId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetVehicleTypes",
            parameters: VendorParameters(vendorId),
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<VehicleType>(command);

        return rows
            .Select(row => new SelectedItems
            {
                Id = row.VehicleCategoryId,
                Value = row.VehicleCategoryName
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SelectedItems>> GetSeatingCapacityAsync(
        int? vendorId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetSeatingCapacity",
            parameters: VendorParameters(vendorId),
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<SeatingCapacity>(command);

        return rows
            .Select(row => new SelectedItems
            {
                Id = row.CapacityId,
                Value = row.CapacityName
            })
            .ToList();
    }

    // @VendorId is sent only when the request carries one, so calls without a vendor
    // reach the stored procedure exactly as they did before the parameter existed.
    private static DynamicParameters? VendorParameters(int? vendorId)
    {
        if (vendorId is null)
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("VendorId", vendorId, DbType.Int32);
        return parameters;
    }
}
