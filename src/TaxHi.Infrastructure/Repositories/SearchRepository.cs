using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using TaxHi.Application.Models;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Models;
using static TaxHi.Infrastructure.Helpers.SearchHelper;

namespace TaxHi.Infrastructure.Repositories;

public sealed class SearchRepository(IDbConnection connection, IConfiguration configuration) : ISearchRepository
{
    /// <summary>ImageBaseUrl + the stored image value, with exactly one slash between them. Absolute URLs are left alone.</summary>
    private string? BuildImageUrl(string? image)
    {
        var baseUrl = configuration["ImageBaseUrl"];
        if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(baseUrl)) return image;
        image = image.Trim();
        if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return image;
        return baseUrl.Trim().TrimEnd('/') + "/" + image.TrimStart('/');
    }

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
        decimal km,
        CancellationToken cancellationToken = default)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var parameters = new DynamicParameters();
        parameters.Add("VendorId", request.VendorId, DbType.Int32);
        parameters.Add("FromDate", request.FromDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("ToDate", request.ToDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("PickupPlaceId", request.PickupPlaceId?.Trim(), DbType.String, size: 300);
        parameters.Add("PickupLatitude", request.PickupLatitude, DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("PickupLongitude", request.PickupLongitude, DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("DropPlaceId", request.DropPlaceId?.Trim(), DbType.String, size: 300);
        parameters.Add("DropLatitude", request.DropLatitude, DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("DropLongitude", request.DropLongitude, DbType.Decimal, precision: 9, scale: 6);
        parameters.Add("Km", km, DbType.Decimal, precision: 10, scale: 2);
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
            commandTimeout: 120,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = (await resultSets.ReadAsync<AvailableCabListing>()).AsList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();
        foreach (var row in rows)
            row.VehicleImageURL = BuildImageUrl(row.VehicleImageURL);

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

    public async Task<PagedResult<SelectedItems>> GetAirportTerminalsAsync(
        int vendorId,
        PaginationRequest request,
        CancellationToken cancellationToken = default)
    {
        var keyword = NormalizeKeyword(request.Keyword);
        var command = new CommandDefinition(
            commandText: "USP_GetAirportTerminals",
            parameters: new { VendorId = vendorId, Keyword = keyword, request.CurrentPage, request.PageSize },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = (await resultSets.ReadAsync<AirportTerminal>())
            .Where(row => !string.IsNullOrWhiteSpace(row.AirportName))
            .Select(row => new SelectedItems { Id = row.AirportTerminalId, Value = row.AirportName.Trim() })
            .ToList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();

        return new PagedResult<SelectedItems>
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
