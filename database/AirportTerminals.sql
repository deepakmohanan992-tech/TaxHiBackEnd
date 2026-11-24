/*
  Airport terminals under a vendor's sector. Additive and re-runnable; changes no table and no existing procedure.

    dbo.USP_GetAirportTerminals @VendorId, @Keyword, @CurrentPage, @PageSize
      result set 1: the page of terminals
      result set 2: CurrentPage, PageSize, TotalRecords, TotalPages  (same shape as the other paged procedures)

  ASSUMED SCHEMA (adjust if your database differs; none of these is defined elsewhere in this repo):
    dbo.TransportVendorMaster:   VendorId, SectorId      <- the vendor's sector
    dbo.AirportTerminalMaster:   AirportTerminalId, AirportName, TerminalName, AirportCode, CityId, SectorId,
                                 Latitude, Longitude, GooglePlaceId, GoogleAddress, IsPopular, DisplayOrder, IsActive
  dbo.TaxhiBEVehicleAirportTerminal is not needed: terminals are listed per sector, not per vehicle.
  @Keyword (optional) matches AirportName, TerminalName or AirportCode.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.USP_GetAirportTerminals
    @VendorId    INT,
    @Keyword     NVARCHAR(100) = NULL,
    @CurrentPage INT = 1,
    @PageSize    INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SectorId INT = (SELECT SectorId FROM dbo.TransportVendorMaster WHERE VendorId = @VendorId);
    DECLARE @Like NVARCHAR(210) = CASE WHEN @Keyword IS NULL THEN NULL
        ELSE N'%' + REPLACE(REPLACE(REPLACE(@Keyword, N'\', N'\\'), N'%', N'\%'), N'_', N'\_') + N'%' END;

    DECLARE @Total INT =
    (
        SELECT COUNT(*)
        FROM dbo.AirportTerminalMaster T
        WHERE T.IsActive = 1
          AND T.SectorId = @SectorId
          AND (@Like IS NULL OR T.AirportName LIKE @Like ESCAPE N'\'
                             OR T.TerminalName LIKE @Like ESCAPE N'\'
                             OR T.AirportCode LIKE @Like ESCAPE N'\')
    );

    SELECT
        T.AirportTerminalId,
        T.AirportName               -- the API returns these two as SelectedItems { Id, Value }
    FROM dbo.AirportTerminalMaster T
    WHERE T.IsActive = 1
      AND T.SectorId = @SectorId
      AND (@Like IS NULL OR T.AirportName LIKE @Like ESCAPE N'\'
                         OR T.TerminalName LIKE @Like ESCAPE N'\'
                         OR T.AirportCode LIKE @Like ESCAPE N'\')
    ORDER BY T.IsPopular DESC, T.DisplayOrder ASC, T.AirportName ASC
    OFFSET (@CurrentPage - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT
        @CurrentPage AS CurrentPage,
        @PageSize    AS PageSize,
        @Total       AS TotalRecords,
        CAST(CEILING(@Total * 1.0 / NULLIF(@PageSize, 0)) AS INT) AS TotalPages;
END
GO
