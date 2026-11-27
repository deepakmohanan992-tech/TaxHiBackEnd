using System.Data;
using System.Xml.Linq;
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
        TripContext? trip = null,
        IReadOnlyList<YardDistance>? yardDistances = null,
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

        // The trip-mode parameters are only sent for a trip mode, so a request without one calls the procedure
        // exactly as before (and keeps working until the updated procedure is deployed).
        if (trip is not null)
        {
            parameters.Add("Mode", trip.Mode, DbType.AnsiString, size: 20);
            parameters.Add("RateAlgorithm", trip.RateAlgorithm, DbType.Int32);
            if (trip.Mode == TripModes.Airport)
            {
                parameters.Add("AirportTerminalId", trip.AirportTerminalId, DbType.Int32);
                parameters.Add("AirportDirection", trip.AirportDirection, DbType.AnsiString, size: 10);
            }
            parameters.Add("YardDistances", BuildYardDistancesXml(yardDistances), DbType.Xml);
        }

        var command = new CommandDefinition(
            commandText: "USP_GetAvailableCabListing",
            parameters: parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        using var resultSets = await connection.QueryMultipleAsync(command);
        var rows = (await resultSets.ReadAsync<AvailableCabListing>()).AsList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();
        foreach (var row in rows)
            row.VehicleImageURL = BuildImageUrl(row.VehicleImageURL);

        // Airport mode: third result set with the charge breakdown of the listed vehicles' brands.
        if (trip?.Mode == TripModes.Airport && !resultSets.IsConsumed)
        {
            var charges = (await resultSets.ReadAsync<AirportChargeRow>()).AsList();
            foreach (var row in rows)
                row.AirportChargeItems = charges
                    .Where(c => c.VendorId == row.VendorId && c.VehicleBrandId == row.VehicleBrandId)
                    .Select(c => new AirportChargeItem { ChargeName = c.ChargeName, Amount = c.Amount })
                    .ToList();
        }

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

    private sealed class AirportChargeRow
    {
        public int VendorId { get; set; }
        public int VehicleBrandId { get; set; }
        public string ChargeName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    /// <summary>&lt;Yards&gt;&lt;Yard Id ToPickupKm ToDropKm/&gt;&lt;/Yards&gt;. A distance that is not known is left out, never written as 0.</summary>
    internal static string BuildYardDistancesXml(IReadOnlyList<YardDistance>? yardDistances) =>
        new XElement("Yards", (yardDistances ?? []).Select(y =>
        {
            var yard = new XElement("Yard", new XAttribute("Id", y.YardId));
            if (y.ToPickupKm is { } toPickup) yard.Add(new XAttribute("ToPickupKm", toPickup));
            return yard;
        })).ToString(SaveOptions.DisableFormatting);

    public async Task<IReadOnlyList<VendorYard>> GetVendorYardsAsync(int? vendorId, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            commandText: "USP_GetVendorYards",
            parameters: new { VendorId = vendorId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<VendorYard>(command)).AsList();
    }

    public async Task<PagedResult<AirportTerminalItem>> GetAirportTerminalsAsync(
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
            .Select(row => new AirportTerminalItem
            {
                Id = row.AirportTerminalId,
                Value = row.AirportName.Trim(),
                Latitude = row.Latitude,
                Longitude = row.Longitude,
                PlaceId = string.IsNullOrWhiteSpace(row.GooglePlaceId) ? null : row.GooglePlaceId.Trim()
            })
            .ToList();
        var pagination = await resultSets.ReadSingleOrDefaultAsync<PaginationMetadata>();

        return new PagedResult<AirportTerminalItem>
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
