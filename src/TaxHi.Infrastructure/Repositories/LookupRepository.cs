using System.Data;
using Dapper;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class LookupRepository(IDbConnection connection) : ILookupRepository
{
    public async Task<IReadOnlyList<SelectedItems>> GetVehicleTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetVehicleTypes",
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
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetSeatingCapacity",
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
}
