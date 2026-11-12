using System.Data;
using Dapper;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Models;
using static TaxHi.Infrastructure.Helpers.SearchHelper;

namespace TaxHi.Infrastructure.Repositories;

public sealed class SearchRepository(IDbConnection connection) : ISearchRepository
{
    public async Task<PagedResult<SelectedItems>> GetOutstationLocationsBySearchAsync(
        LocationSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var command = new CommandDefinition(
            commandText: "USP_GetOutstationLocationsBySearch",
            parameters: new
            {
                Keyword = keyword,
                request.CurrentPage,
                request.PageSize
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = await resultSets.ReadAsync<OutstationLocation>();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();
        var locations = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.LocationName))
            .Select(row => new SelectedItems
            {
                Id = row.CityId,
                Value = row.LocationName!.Trim()
            })
            .DistinctBy(item => new { item.Id, item.Value })
            .ToList();

        return new PagedResult<SelectedItems>
        {
            Items = locations,
            CurrentPage = pagination?.CurrentPage ?? request.CurrentPage,
            PageSize = pagination?.PageSize ?? request.PageSize,
            TotalItems = pagination?.TotalRecords ?? locations.Count,
            TotalPages = pagination?.TotalPages ?? (locations.Count == 0 ? 0 : 1),
            Keyword = keyword
        };
    }

    public async Task<PagedResult<AvailableCabListing>> GetAvailableCabListingAsync(
        AvailableCabListingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var parameters = new DynamicParameters();
        parameters.Add("VendorId", request.VendorId, DbType.Int32);
        parameters.Add("FromDate", request.FromDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("ToDate", request.ToDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("LocationFrom", request.LocationFrom, DbType.Int32);
        parameters.Add("LocationTo", request.LocationTo, DbType.Int32);
        parameters.Add("VehicleType", ToCsv(request.VehicleType), DbType.String);
        parameters.Add("Seats", request.Seats, DbType.Int32);
        parameters.Add("Recommended", request.Recommended, DbType.Int32);
        parameters.Add("CurrentPage", request.CurrentPage, DbType.Int32);
        parameters.Add("PageSize", request.PageSize, DbType.Int32);
        parameters.Add("Keyword", keyword, DbType.String);

        var command = new CommandDefinition(
            commandText: "USP_GetAvailableCabListing",
            parameters: parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = (await resultSets.ReadAsync<AvailableCabListing>()).AsList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();

        return new PagedResult<AvailableCabListing>
        {
            Items = rows,
            CurrentPage = pagination?.CurrentPage ?? request.CurrentPage,
            PageSize = pagination?.PageSize ?? request.PageSize,
            TotalItems = pagination?.TotalRecords ?? rows.Count,
            TotalPages = pagination?.TotalPages ?? (rows.Count == 0 ? 0 : 1),
            Keyword = keyword
        };
    }

}
