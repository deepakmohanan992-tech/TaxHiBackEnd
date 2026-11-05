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
            commandText: "usp_GetOutstationLocationsBySearch",
            parameters: new { SearchWord = searchWord },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<OutstationLocationRow>(command);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.LocationName))
            .Select(row => new SelectedItems
            {
                Id = row.CityId,
                Value = row.LocationName!.Trim()
            })
            .DistinctBy(item => new { item.Id, item.Value })
            .ToList();
    }
}
