using System.Data;
using Dapper;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

namespace TaxHi.Infrastructure.Repositories;

public sealed class SearchRepository(IDbConnection connection) : ISearchRepository
{
    public async Task<IReadOnlyList<SelectedItems>> GetOutstationLocationsBySearchAsync(
        string searchWord,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetOutstationLocationsBySearch",
            parameters: new { SearchWord = searchWord },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<OutstationLocationRow>(command);

        return [.. rows
            .Where(row => !string.IsNullOrWhiteSpace(row.LocationName))
            .Select(row => new SelectedItems
            {
                Id = row.CityId,
                Value = row.LocationName!.Trim()
            })
            .DistinctBy(item => new { item.Id, item.Value })];
    }

    public async Task<IReadOnlyList<AvailableCabListing>> GetAvailableCabListingAsync(
        int? vendorId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int? locationFrom,
        int? locationTo,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("VendorId", vendorId, DbType.Int32);
        parameters.Add("FromDate", fromDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("ToDate", toDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("LocationFrom", locationFrom, DbType.Int32);
        parameters.Add("LocationTo", locationTo, DbType.Int32);

        var command = new CommandDefinition(
            commandText: "USP_GetAvailableCabListing",
            parameters: parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<AvailableCabListing>(command);

        return [.. rows];
    }
}
