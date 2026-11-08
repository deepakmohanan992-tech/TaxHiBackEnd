using System.Data;
using Dapper;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;

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

        var rows = await connection.QueryAsync<OutstationLocation>(command);
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
            CurrentPage = request.CurrentPage,
            PageSize = request.PageSize,
            TotalItems = locations.Count,
            TotalPages = locations.Count == 0 ? 0 : 1,
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
        parameters.Add("VehicleType", request.VehicleType, DbType.Int32);
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

        var rows = (await connection.QueryAsync<AvailableCabListing>(command)).AsList();

        return new PagedResult<AvailableCabListing>
        {
            Items = rows,
            CurrentPage = request.CurrentPage,
            PageSize = request.PageSize,
            TotalItems = rows.Count,
            TotalPages = rows.Count == 0 ? 0 : 1,
            Keyword = keyword
        };
    }

    private static string? NormalizeKeyword(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
}
