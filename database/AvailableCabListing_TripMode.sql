/*
  USP_GetAvailableCabListing - trip-mode aware version.

  ALTER of the existing procedure. Existing parameters, filters, pagination, sorting and result columns are unchanged;
  new parameters all default to NULL, and with @Mode = NULL (or 'Local') the procedure behaves exactly as before.

  New parameters
    @Mode               Oneway | RoundTrip | Airport | Local.   Local is reserved: it is accepted and priced by the legacy @Km logic only.
    @RateAlgorithm      100 | 150 | 200 (default 100). Scales the yard-return leg (see @ReturnFactor below).
    @AirportTerminalId  Airport mode only.
    @AirportDirection   Airport mode only: 'Pickup' (customer starts at the airport) or 'Drop' (customer ends at the airport).
    @YardDistances      XML computed by the API through the existing Google road-distance service:
                          <Yards><Yard Id="12" ToPickupKm="10.5"/></Yards>
                        Only the yard -> pickup distance is supplied (never yard -> drop). A missing attribute means "not resolved"; it is never treated as 0.

  Calculation (@Km = customer journey, pickup -> drop, calculated by the API)
    f = return factor of the selected algorithm.  100% -> 1.0,  150% -> 0.5,  200% -> 1.0   (taken from the agreed examples;
        this is the only place to change if the rule is revised).
    Oneway          yard->pickup + journey + f x (return to yard)
    Airport Pickup  yard->pickup + journey                      (vehicle starts at its yard, serves the airport pick-up)
    Airport Drop    journey + (return to yard)                  (vehicle returns to its yard after the airport drop)
    "Return to yard" distance: drop->yard is NOT measured; it is taken as the yard->pickup road distance (same yard leg as the round trip).
    RoundTrip, 1 day   yard->pickup + 2 x journey + f x (pickup->yard)
    RoundTrip, N days  day 1   = MAX(yard->pickup + journey, MinimumKM)
                       middle  = MinimumKM per day
                       day N   = MAX(journey + f x (pickup->yard), MinimumKM)
  Fare
    Included km = MinimumKM x billed days (billed days = number of days for RoundTrip, otherwise 1)
    Base        = MinimumKMRate x billed days
    Extra km    = MAX(0, total km - included km);  Extra amount = extra km x ExtraKMRate
    OriginalAmount = Base + Extra amount;  GST = OriginalAmount x GST% (existing rule)
    AirportCharges = sum of active TaxhiBEBrandAirportCharge rows for the vendor + brand + terminal (one row per ChargeName)
    FinalFare   = OriginalAmount + GST + AirportCharges
  Yard of a vehicle: an active yard of the vehicle's vendor whose SectorId is NULL or equals the vehicle's AreaOfOperation,
    same-sector yards first, then the shortest yard movement. No usable yard -> vehicle is 'Unavailable' (YardDistanceStatus = 'YardDistanceMissing').
  Category listing: with a trip mode, one row per vendor + vehicle category: the cheapest available vehicle (FinalFare, then nearest yard).
    A category whose vehicles are all unavailable is returned as one 'Unavailable' row.
*/
USE [MarvelAutomation]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[USP_GetAvailableCabListing]
(
    @VendorId INT = NULL,
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,

    -- Pagination
    @CurrentPage INT = 1,
    @PageSize INT = 10,
    @Keyword NVARCHAR(100) = NULL,

    -- Vehicle filters
    @VehicleType VARCHAR(200) = NULL,
    @Seats INT = -1,
    @Recommended INT = NULL,

    -- Pickup / Drop place details
    @PickupPlaceId NVARCHAR(300) = NULL,
    @PickupLatitude DECIMAL(9,6) = NULL,
    @PickupLongitude DECIMAL(9,6) = NULL,
    @DropPlaceId NVARCHAR(300) = NULL,
    @DropLatitude DECIMAL(9,6) = NULL,
    @DropLongitude DECIMAL(9,6) = NULL,

    -- API calculated distance (customer journey, pickup -> drop)
    @Km DECIMAL(10,2) = 0,

    -- Trip mode
    @Mode VARCHAR(20) = NULL,
    @RateAlgorithm INT = 100,
    @AirportTerminalId INT = NULL,
    @AirportDirection VARCHAR(10) = NULL,
    @YardDistances XML = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------------------------
    -- 1. Validate Input Parameters
    ------------------------------------------------

    SET @CurrentPage =
        CASE
            WHEN ISNULL(@CurrentPage, 0) < 1 THEN 1
            ELSE @CurrentPage
        END;

    SET @PageSize =
        CASE
            WHEN ISNULL(@PageSize, 0) < 1 THEN 10
            ELSE @PageSize
        END;

    SET @Keyword = NULLIF(LTRIM(RTRIM(@Keyword)), '');
    SET @VehicleType = NULLIF(LTRIM(RTRIM(@VehicleType)), '');
    SET @PickupPlaceId = NULLIF(LTRIM(RTRIM(@PickupPlaceId)), '');
    SET @DropPlaceId = NULLIF(LTRIM(RTRIM(@DropPlaceId)), '');

    SET @Km =
        CASE
            WHEN ISNULL(@Km, 0) > 0 THEN @Km
            ELSE 0
        END;

    IF @VehicleType = '0'
        SET @VehicleType = NULL;

    IF @Recommended NOT IN (0, 1)
        SET @Recommended = NULL;

    ------------------------------------------------
    -- 1a. Trip mode
    ------------------------------------------------

    SET @Mode =
        CASE UPPER(NULLIF(LTRIM(RTRIM(@Mode)), ''))
            WHEN 'ONEWAY'    THEN 'Oneway'
            WHEN 'ROUNDTRIP' THEN 'RoundTrip'
            WHEN 'AIRPORT'   THEN 'Airport'
            WHEN 'LOCAL'     THEN 'Local'
            ELSE NULLIF(LTRIM(RTRIM(@Mode)), '')
        END;

    IF @Mode IS NOT NULL AND @Mode NOT IN ('Oneway', 'RoundTrip', 'Airport', 'Local')
    BEGIN
        RAISERROR('Mode must be Oneway, RoundTrip, Airport or Local.', 16, 1);
        RETURN;
    END;

    -- Local is reserved for later: it keeps the legacy @Km pricing and none of the trip-mode logic below.
    DECLARE @UseTripMode BIT =
        CASE WHEN @Mode IN ('Oneway', 'RoundTrip', 'Airport') THEN 1 ELSE 0 END;

    SET @RateAlgorithm = ISNULL(@RateAlgorithm, 100);
    SET @AirportDirection =
        CASE UPPER(NULLIF(LTRIM(RTRIM(@AirportDirection)), ''))
            WHEN 'PICKUP' THEN 'Pickup'
            WHEN 'DROP'   THEN 'Drop'
            ELSE NULLIF(LTRIM(RTRIM(@AirportDirection)), '')
        END;

    -- Share of the yard-return leg that is charged for each rate algorithm.
    DECLARE @ReturnFactor DECIMAL(5,2) = NULL;

    IF @UseTripMode = 1
    BEGIN
        SET @ReturnFactor =
            CASE @RateAlgorithm
                WHEN 100 THEN 1.00
                WHEN 150 THEN 0.50
                WHEN 200 THEN 1.00
            END;

        IF @ReturnFactor IS NULL
        BEGIN
            RAISERROR('RateAlgorithm must be 100, 150 or 200.', 16, 1);
            RETURN;
        END;

        IF @Km <= 0
        BEGIN
            RAISERROR('The customer journey distance is required for the selected mode.', 16, 1);
            RETURN;
        END;

        IF @Mode = 'Airport'
           AND (@AirportTerminalId IS NULL OR @AirportDirection NOT IN ('Pickup', 'Drop') OR @AirportDirection IS NULL)
        BEGIN
            RAISERROR('Airport mode requires AirportTerminalId and AirportDirection (Pickup or Drop).', 16, 1);
            RETURN;
        END;
    END;

    DECLARE @YardDist TABLE
    (
        YardId INT PRIMARY KEY,
        ToPickupKm DECIMAL(10,2) NULL
    );

    IF @UseTripMode = 1 AND @YardDistances IS NOT NULL
    BEGIN
        INSERT INTO @YardDist (YardId, ToPickupKm)
        SELECT
            X.YardId,
            MAX(X.ToPickupKm)
        FROM
        (
            SELECT
                Y.value('@Id', 'INT') AS YardId,
                Y.value('@ToPickupKm', 'DECIMAL(10,2)') AS ToPickupKm
            FROM @YardDistances.nodes('/Yards/Yard') AS T(Y)
        ) X
        WHERE X.YardId IS NOT NULL
        GROUP BY X.YardId;
    END;

    DECLARE @VehicleTypeList TABLE
    (
        VehicleCategoryId INT PRIMARY KEY
    );

    IF @VehicleType IS NOT NULL
    BEGIN
        DECLARE @VehicleTypeXml XML =
            CAST(
                '<x>' + REPLACE(@VehicleType, ',', '</x><x>') + '</x>'
                AS XML
            );

        INSERT INTO @VehicleTypeList (VehicleCategoryId)
        SELECT DISTINCT
            TRY_CAST(LTRIM(RTRIM(N.value('.', 'VARCHAR(20)'))) AS INT)
        FROM @VehicleTypeXml.nodes('/x') AS T(N)
        WHERE
            TRY_CAST(LTRIM(RTRIM(N.value('.', 'VARCHAR(20)'))) AS INT) IS NOT NULL
            AND TRY_CAST(LTRIM(RTRIM(N.value('.', 'VARCHAR(20)'))) AS INT) > 0;
    END;

    DECLARE @SearchKeyword NVARCHAR(202);
    DECLARE @AvailabilityFromDate DATE;
    DECLARE @AvailabilityToDate DATE;
    DECLARE @Days INT;
    DECLARE @NowUtc DATETIME2(3) = SYSUTCDATETIME();

    DECLARE @StartRow BIGINT;
    DECLARE @TotalRecords BIGINT;
    DECLARE @TotalPages BIGINT;

    SET @SearchKeyword =
        CASE
            WHEN @Keyword IS NOT NULL THEN '%' + @Keyword + '%'
            ELSE NULL
        END;

    ------------------------------------------------
    -- 2. Set Availability Dates
    ------------------------------------------------

    SET @AvailabilityFromDate =
        ISNULL(@FromDate, ISNULL(@ToDate, CAST(GETDATE() AS DATE)));

    SET @AvailabilityToDate =
        ISNULL(@ToDate, ISNULL(@FromDate, CAST(GETDATE() AS DATE)));

    IF @AvailabilityFromDate > @AvailabilityToDate
    BEGIN
        RAISERROR('FromDate cannot be greater than ToDate.', 16, 1);
        RETURN;
    END;

    -- Calendar days of the booking (From and To both count).
    SET @Days = DATEDIFF(DAY, @AvailabilityFromDate, @AvailabilityToDate) + 1;

    -- Pickup and drop locations are mandatory.
    IF NOT
    (
        (@PickupPlaceId IS NOT NULL OR
         (@PickupLatitude IS NOT NULL AND @PickupLongitude IS NOT NULL))
        AND
        (@DropPlaceId IS NOT NULL OR
         (@DropLatitude IS NOT NULL AND @DropLongitude IS NOT NULL))
    )
    BEGIN
        RAISERROR('Pickup and drop locations are required.', 16, 1);
        RETURN;
    END;

    SET @StartRow =
        (CAST(@CurrentPage AS BIGINT) - 1) * @PageSize;

    ------------------------------------------------
    -- 3. Sector-to-City Mapping CTE
    ------------------------------------------------

    ;WITH SectorCity AS
    (
        SELECT
            SM.SectorId,
            SM.CityId
        FROM TransportSectorMaster SM
        WHERE
            SM.IsActive = 1
            AND SM.CityId IS NOT NULL

        UNION

        SELECT
            SL.SectorId,
            SL.CityId
        FROM TransportSectorLocation SL
        INNER JOIN TransportSectorMaster SM
            ON SM.SectorId = SL.SectorId
        WHERE
            SM.IsActive = 1
            AND SL.CityId IS NOT NULL
    ),

    ------------------------------------------------
    -- 4. Base Physical Vehicles CTE
    ------------------------------------------------

    BaseVehicles AS
    (
        SELECT
            VD.VehicleDetailsId,
            VVM.VehicleId,
            VVM.VendorId,
            VVM.VehicleCategoryId,
            VD.VehicleBrandId,
            VD.AreaOfOperation,
            VB.VehicleName,
            VC.SeatingCapacity,
            VB.IsAirCondition,

            BookingConflict.IsBooked,
            BookingConflict.NextAvailableDate,

            ------------------------------------------------
            -- Vendor and Vehicle Sector Validation
            -- Vendor must cover the entire vehicle sector.
            ------------------------------------------------

            CASE
                WHEN
                    NOT EXISTS
                    (
                        SELECT 1
                        FROM TransportVendorSectorDetails TVSD
                        WHERE TVSD.VendorId = VVM.VendorId
                    )

                    OR VD.AreaOfOperation IS NULL

                    OR NOT EXISTS
                    (
                        SELECT 1
                        FROM SectorCity VehicleSC
                        WHERE
                            VehicleSC.SectorId = VD.AreaOfOperation

                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM TransportVendorSectorDetails TVSD

                                INNER JOIN SectorCity VendorSC
                                    ON VendorSC.SectorId =
                                       TVSD.TransportVendorSectorId

                                WHERE
                                    TVSD.VendorId = VVM.VendorId
                                    AND VendorSC.CityId = VehicleSC.CityId
                            )
                    )

                THEN 1
                ELSE 0
            END AS IsSectorValid

        FROM TransportVendorVehilceDetails VD

        INNER JOIN TransportVendorVehicleMaster VVM
            ON VVM.VehicleId = VD.VehicleId

        INNER JOIN TransportVendorMaster VM
            ON VM.VendorId = VVM.VendorId

        INNER JOIN TransportVehicleCategory VC
            ON VC.VehicleCategoryId = VVM.VehicleCategoryId

        INNER JOIN TransportVehicleBrand VB
            ON VB.VehicleBrandId = VD.VehicleBrandId

        ------------------------------------------------
        -- Booking Availability Check
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CASE
                    WHEN COUNT(*) > 0 THEN 1
                    ELSE 0
                END AS IsBooked,

                CASE
                    WHEN COUNT(*) > 0 THEN
                        DATEADD
                        (
                            DAY,
                            1,
                            MAX
                            (
                                COALESCE
                                (
                                    CAST(T.ToDate AS DATE),
                                    CAST(T.FromDate AS DATE)
                                )
                            )
                        )
                    ELSE NULL
                END AS NextAvailableDate

            FROM VehicleTripMaster T

            -- A trip created for an online booking only blocks the vehicle while that booking is Confirmed (paid) or
            -- Pending with an ACTIVE hold. Failed, cancelled, expired or released bookings free the vehicle.
            -- Trips not linked to an online booking (created elsewhere) block exactly as before.
            LEFT JOIN VehicleBookingMaster HVBM
                ON HVBM.VehicleTripMasterId = T.VehicleTripMasterId

            LEFT JOIN TaxHiBEBookings HB
                ON HB.VehicleBookingMasterId = HVBM.VehicleBookingMasterId

            WHERE
                T.VehicleDetailsId = VD.VehicleDetailsId
                AND T.VendorId = VVM.VendorId
                AND T.FromDate IS NOT NULL

                AND
                (
                    HB.BookingId IS NULL
                    OR HB.BookingStatus = 'Confirmed'
                    OR (HB.BookingStatus = 'Pending' AND HB.HoldExpiresAtUtc > @NowUtc)
                )

                AND
                (
                    (
                        T.ToDate IS NOT NULL

                        AND CAST(T.FromDate AS DATE)
                            <= @AvailabilityToDate

                        AND CAST(T.ToDate AS DATE)
                            >= @AvailabilityFromDate
                    )

                    OR

                    (
                        T.ToDate IS NULL

                        AND CAST(T.FromDate AS DATE)
                            BETWEEN @AvailabilityFromDate
                            AND @AvailabilityToDate
                    )
                )

        ) BookingConflict

        WHERE
            VD.IsActive = 1
            AND ISNULL(VD.IsDeActivate, 0) = 0
            AND VVM.IsActive = 1
            AND VM.IsActive = 1
            AND VC.IsActive = 1
            AND VB.IsActive = 1

            AND
            (
                @VendorId IS NULL
                OR VVM.VendorId = @VendorId
            )

            AND
            (
                @VehicleType IS NULL
                OR EXISTS
                (
                    SELECT 1
                    FROM @VehicleTypeList VTL
                    WHERE
                        VTL.VehicleCategoryId = VVM.VehicleCategoryId
                )
            )

            AND
            (
                @Seats = -1
                OR @Seats IS NULL
                OR VC.SeatingCapacity = @Seats
            )
    ),

    ------------------------------------------------
    -- 5. Vehicle Details, Pricing and Image Mapping
    ------------------------------------------------

    VehicleListing AS
    (
        SELECT
            BV.VendorId,
            BV.VehicleId,
            BV.VehicleDetailsId,
            BV.VehicleCategoryId,
            BV.VehicleBrandId,
            BV.VehicleName,

            ImageInfo.VehicleImageURL,

            DistanceInfo.DistanceInKm,

            CASE
                WHEN BV.SeatingCapacity IS NULL THEN
                    CASE
                        WHEN BV.IsAirCondition = 1 THEN 'AC Cab'
                        WHEN BV.IsAirCondition = 0 THEN 'Non-AC Cab'
                        ELSE 'Cab'
                    END
                ELSE
                    CAST(BV.SeatingCapacity AS VARCHAR(10))
                    + ' seater '
                    +
                    CASE
                        WHEN BV.IsAirCondition = 1 THEN 'AC Cab'
                        WHEN BV.IsAirCondition = 0 THEN 'Non-AC Cab'
                        ELSE 'Cab'
                    END
            END AS VehicleDescription,

            'Diesel' AS FuelType,

            CAST(NULL AS DECIMAL(18,2)) AS DiscountPercentage,

            -- Distance fare (minimum km rate + extra km). Airport charges are NOT part of it.
            Pricing.OriginalAmount AS OriginalAmount,

            CAST(NULL AS DECIMAL(18,2)) AS DiscountedAmount,

            Rate.GSTPercentage AS GSTPercentage,

            GstCalc.GSTAmount AS GSTAmount,

            CASE
                WHEN @UseTripMode = 1 THEN TripDist.IncludedKm
                ELSE Rate.MinimumKM
            END AS IncludedKilometres,
            Rate.ExtraKMRate AS PostLimitChargePerKM,
            Rate.Remarks AS Inclusions,

            ------------------------------------------------
            -- Availability Status
            -- In a trip mode a vehicle whose yard distance is unknown is never offered:
            -- its fare cannot be calculated correctly.
            ------------------------------------------------

            CASE
                WHEN
                    BV.IsBooked = 1
                    OR BV.IsSectorValid = 0
                    OR (@UseTripMode = 1 AND YardInfo.YardId IS NULL)
                THEN 'Unavailable'
                ELSE 'Available'
            END AS AvailabilityStatus,

            ------------------------------------------------
            -- Next Available Date
            ------------------------------------------------

            CASE
                WHEN
                    BV.IsBooked = 1
                    AND BV.NextAvailableDate IS NOT NULL
                THEN
                    'Available from '
                    + CONVERT(VARCHAR(10), BV.NextAvailableDate, 105)
                ELSE NULL
            END AS AvailableFrom,

            ------------------------------------------------
            -- Trip-mode details (all NULL outside a trip mode)
            ------------------------------------------------

            CASE WHEN @UseTripMode = 1 THEN @Mode END AS TripMode,
            CASE WHEN @UseTripMode = 1 THEN @RateAlgorithm END AS RateAlgorithm,
            YardInfo.YardId AS YardId,
            YardInfo.YardName AS YardName,
            YardInfo.ToPickupKm AS YardToPickupKm,
            CASE
                WHEN @UseTripMode = 0 THEN NULL
                WHEN YardInfo.YardId IS NULL THEN 'YardDistanceMissing'
                ELSE 'OK'
            END AS YardDistanceStatus,
            CASE WHEN @UseTripMode = 1 THEN @Km END AS CustomerJourneyKm,
            CAST(TripDist.TotalKm AS DECIMAL(18,2)) AS TotalCalculatedKm,
            CASE WHEN @UseTripMode = 1 THEN DayCalc.BilledDays END AS BilledDays,
            CAST(TripExtra.ExtraKm AS DECIMAL(18,2)) AS ExtraKm,
            CAST(TripExtra.ExtraKm * ISNULL(Rate.ExtraKMRate, 0) AS DECIMAL(18,2)) AS ExtraKmAmount,
            AirportInfo.AirportCharges AS AirportCharges,
            CASE
                WHEN @UseTripMode = 1 AND Pricing.OriginalAmount IS NOT NULL
                THEN CAST
                     (
                         Pricing.OriginalAmount
                         + ISNULL(GstCalc.GSTAmount, 0)
                         + ISNULL(AirportInfo.AirportCharges, 0)
                         AS DECIMAL(18,2)
                     )
                ELSE NULL
            END AS FinalFare

        FROM BaseVehicles BV

        ------------------------------------------------
        -- Customer journey distance (pickup -> drop)
        -- API-calculated @Km is the only distance source.
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                @Km AS DistanceInKm
        ) DistanceInfo

        ------------------------------------------------
        -- Rate Details
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT TOP 1
                RD.RateDetailId,
                RD.MinimumKM,
                RD.MinimumKMRate,
                RD.ExtraKMRate,
                RD.GSTPercentage,
                RD.Remarks

            FROM TransportVendorRateDetails RD

            WHERE
                RD.VendorId = BV.VendorId
                AND RD.VehicleCategoryId = BV.VehicleCategoryId
                AND RD.IsActive = 1

                AND
                (
                    (
                        RD.ContractFromDate <= @AvailabilityToDate
                        AND RD.ContractToDate >= @AvailabilityFromDate
                    )
                    OR RD.ContractFromDate IS NULL
                )

            ORDER BY
                CASE
                    WHEN RD.ContractFromDate IS NOT NULL THEN 0
                    ELSE 1
                END,
                RD.ContractFromDate DESC,
                RD.RateDetailId DESC

        ) Rate

        ------------------------------------------------
        -- Legacy fare (no trip mode / Local):
        --   @Km <= MinimumKM : MinimumKMRate
        --   @Km >  MinimumKM : MinimumKMRate + (Extra KM * ExtraKMRate)
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CAST
                (
                    CASE
                        WHEN Rate.MinimumKMRate IS NULL
                            THEN NULL

                        WHEN ISNULL(DistanceInfo.DistanceInKm, 0)
                             <= ISNULL(Rate.MinimumKM, 0)
                            THEN Rate.MinimumKMRate

                        ELSE
                            Rate.MinimumKMRate
                            +
                            (
                                (
                                    DistanceInfo.DistanceInKm
                                    - Rate.MinimumKM
                                )
                                * ISNULL(Rate.ExtraKMRate, 0)
                            )
                    END
                    AS DECIMAL(18,2)
                ) AS VehicleRate

        ) PriceInfo

        ------------------------------------------------
        -- Vehicle yard (trip modes only)
        -- Active yard of the vehicle's vendor, in the vehicle's sector (or a sector-less yard), whose
        -- distances were resolved by the API. Same-sector yards first, then the shortest yard movement.
        -- Yards with an unresolved distance are skipped; nothing is ever assumed to be 0 km.
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT TOP 1
                Y.YardId,
                Y.YardName,
                YD.ToPickupKm

            FROM TransportVendorYard Y

            INNER JOIN @YardDist YD
                ON YD.YardId = Y.YardId

            WHERE
                @UseTripMode = 1
                AND Y.VendorId = BV.VendorId
                AND Y.IsActive = 1
                AND (Y.SectorId IS NULL OR Y.SectorId = BV.AreaOfOperation)
                AND YD.ToPickupKm IS NOT NULL

            ORDER BY
                CASE WHEN Y.SectorId = BV.AreaOfOperation THEN 0 ELSE 1 END,

                -- Oneway and RoundTrip also return to the yard (scaled by the algorithm); other legs are yard -> pickup only.
                CASE
                    WHEN @Mode IN ('Oneway', 'RoundTrip')
                        THEN YD.ToPickupKm * (1 + @ReturnFactor)
                    ELSE YD.ToPickupKm
                END,

                Y.YardId

        ) YardInfo

        ------------------------------------------------
        -- Billing days and per-day figures (trip modes)
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CASE
                    WHEN @UseTripMode = 1 AND @Mode = 'RoundTrip' THEN @Days
                    ELSE 1
                END AS BilledDays,

                CAST(ISNULL(Rate.MinimumKM, 0) AS DECIMAL(18,2)) AS MinKmPerDay,

                -- Multi-day round trip: first day yard -> pickup -> drop, last day drop -> pickup -> yard.
                YardInfo.ToPickupKm + @Km AS FirstDayKm,
                @Km + @ReturnFactor * YardInfo.ToPickupKm AS LastDayKm

        ) DayCalc

        ------------------------------------------------
        -- Total calculated distance and included km (trip modes)
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CAST
                (
                    CASE
                        WHEN @UseTripMode = 0 OR YardInfo.YardId IS NULL
                            THEN NULL

                        WHEN @Mode = 'Oneway'
                            THEN YardInfo.ToPickupKm
                               + @Km
                               + @ReturnFactor * YardInfo.ToPickupKm

                        WHEN @Mode = 'Airport' AND @AirportDirection = 'Pickup'
                            THEN YardInfo.ToPickupKm + @Km

                        WHEN @Mode = 'Airport'
                            THEN @Km + YardInfo.ToPickupKm

                        WHEN @Mode = 'RoundTrip' AND @Days <= 1
                            THEN YardInfo.ToPickupKm
                               + 2 * @Km
                               + @ReturnFactor * YardInfo.ToPickupKm

                        WHEN @Mode = 'RoundTrip'
                            THEN
                                CASE WHEN DayCalc.FirstDayKm > DayCalc.MinKmPerDay
                                     THEN DayCalc.FirstDayKm ELSE DayCalc.MinKmPerDay END
                                + (@Days - 2) * DayCalc.MinKmPerDay
                                + CASE WHEN DayCalc.LastDayKm > DayCalc.MinKmPerDay
                                       THEN DayCalc.LastDayKm ELSE DayCalc.MinKmPerDay END
                    END
                    AS DECIMAL(18,2)
                ) AS TotalKm,

                DayCalc.MinKmPerDay * DayCalc.BilledDays AS IncludedKm

        ) TripDist

        OUTER APPLY
        (
            SELECT
                CASE
                    WHEN TripDist.TotalKm IS NULL THEN NULL
                    WHEN TripDist.TotalKm > TripDist.IncludedKm
                        THEN TripDist.TotalKm - TripDist.IncludedKm
                    ELSE 0
                END AS ExtraKm

        ) TripExtra

        ------------------------------------------------
        -- Distance fare: trip-mode fare when a trip mode is active, legacy fare otherwise.
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CAST
                (
                    CASE
                        WHEN @UseTripMode = 0
                            THEN PriceInfo.VehicleRate

                        WHEN TripDist.TotalKm IS NULL
                             OR Rate.MinimumKMRate IS NULL
                            THEN NULL

                        ELSE
                            Rate.MinimumKMRate * DayCalc.BilledDays
                            + TripExtra.ExtraKm * ISNULL(Rate.ExtraKMRate, 0)
                    END
                    AS DECIMAL(18,2)
                ) AS OriginalAmount

        ) Pricing

        OUTER APPLY
        (
            SELECT
                CASE
                    WHEN
                        Pricing.OriginalAmount IS NOT NULL
                        AND Rate.GSTPercentage IS NOT NULL
                    THEN
                        CAST
                        (
                            Pricing.OriginalAmount
                            * Rate.GSTPercentage / 100.0
                            AS DECIMAL(18,2)
                        )
                    ELSE NULL
                END AS GSTAmount

        ) GstCalc

        ------------------------------------------------
        -- Airport charges (Airport mode only), kept apart from the distance fare.
        -- Active rows for vendor + brand + terminal; one row per ChargeName (lowest SortOrder wins).
        -- No applicable charge gives 0, not NULL.
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT
                CASE
                    WHEN @Mode = 'Airport'
                        THEN CAST(ISNULL(SUM(C.Amount), 0) AS DECIMAL(18,2))
                END AS AirportCharges

            FROM
            (
                SELECT
                    AC.Amount,

                    ROW_NUMBER() OVER
                    (
                        PARTITION BY AC.ChargeName
                        ORDER BY AC.SortOrder, AC.ChargeId
                    ) AS RowNum

                FROM TaxhiBEBrandAirportCharge AC

                WHERE
                    @Mode = 'Airport'
                    AND AC.VendorId = BV.VendorId
                    AND AC.VehicleBrandId = BV.VehicleBrandId
                    AND AC.AirportTerminalId = @AirportTerminalId
                    AND AC.IsActive = 1

            ) C

            WHERE C.RowNum = 1

        ) AirportInfo

        ------------------------------------------------
        -- Image Details
        ------------------------------------------------

        OUTER APPLY
        (
            SELECT TOP 1
                COALESCE
                (
                    AD.SpiralDeskAttachmentPath,
                    AD.BannerImageLocationName,
                    AD.AttachmentThumbName,
                    AD.AttachmentName
                ) AS VehicleImageURL

            FROM AttachmentDetail AD

            WHERE
                AD.VehicleBrandId = BV.VehicleBrandId

            ORDER BY
                ISNULL(AD.IsDefault, 0) DESC,
                AD.AttachmentId DESC

        ) ImageInfo
    )

    ------------------------------------------------
    -- 6. Deduplicate and Save to Temp Table
    --    Trip modes: keep one vehicle per vendor + category (the best eligible one).
    ------------------------------------------------

    SELECT
        VendorId,
        VehicleId,
        VehicleName,
        VehicleDetailsId,
        VehicleCategoryId,
        VehicleBrandId,
        VehicleImageURL,
        VehicleDescription,
        FuelType,
        DistanceInKm,
        DiscountPercentage,
        OriginalAmount,
        DiscountedAmount,
        GSTPercentage,
        GSTAmount,
        IncludedKilometres,
        PostLimitChargePerKM,
        Inclusions,
        AvailabilityStatus,
        AvailableFrom,
        TripMode,
        RateAlgorithm,
        YardId,
        YardName,
        YardToPickupKm,
        YardDistanceStatus,
        CustomerJourneyKm,
        TotalCalculatedKm,
        BilledDays,
        ExtraKm,
        ExtraKmAmount,
        AirportCharges,
        FinalFare

    INTO #FilteredResults

    FROM
    (
        SELECT
            *,

            -- Availability is decided first (booked / sector / yard), then the lowest final fare, then the nearest yard.
            ROW_NUMBER() OVER
            (
                PARTITION BY VendorId, VehicleCategoryId
                ORDER BY
                    CASE WHEN AvailabilityStatus = 'Available' THEN 0 ELSE 1 END,
                    CASE WHEN FinalFare IS NULL THEN 1 ELSE 0 END,
                    FinalFare,
                    CASE WHEN YardToPickupKm IS NULL THEN 1 ELSE 0 END,
                    YardToPickupKm,
                    VehicleDetailsId
            ) AS CategoryRank

        FROM
        (
            SELECT
                *,

                ROW_NUMBER() OVER
                (
                    PARTITION BY VendorId, VehicleDetailsId
                    ORDER BY
                        CASE
                            WHEN AvailabilityStatus = 'Available' THEN 0
                            ELSE 1
                        END
                ) AS RowNum

            FROM VehicleListing

        ) DistinctList

        WHERE
            DistinctList.RowNum = 1

            AND
            (
                @SearchKeyword IS NULL
                OR VehicleName LIKE @SearchKeyword
                OR VehicleDescription LIKE @SearchKeyword
                OR FuelType LIKE @SearchKeyword
            )

    ) Ranked

    WHERE
        @UseTripMode = 0
        OR Ranked.CategoryRank = 1;

    ------------------------------------------------
    -- 7. Calculate Pagination Totals
    ------------------------------------------------

    SELECT
        @TotalRecords = COUNT_BIG(*)
    FROM #FilteredResults;

    SET @TotalPages =
        CASE
            WHEN @TotalRecords = 0 THEN 0
            ELSE CEILING
            (
                CAST(@TotalRecords AS DECIMAL(18,2))
                / @PageSize
            )
        END;

    ------------------------------------------------
    -- 8. Result Set 1: Vehicle Listing
    ------------------------------------------------

    SELECT
        P.*

    INTO #Page

    FROM
    (
        SELECT
            *,

            ROW_NUMBER() OVER
            (
                ORDER BY
                    CASE
                        WHEN AvailabilityStatus = 'Available'
                        THEN 0
                        ELSE 1
                    END ASC,

                    CASE
                        WHEN
                            @Recommended IS NOT NULL
                            AND OriginalAmount IS NULL
                        THEN 1
                        ELSE 0
                    END ASC,

                    CASE
                        WHEN @Recommended = 0
                        THEN OriginalAmount
                    END ASC,

                    CASE
                        WHEN @Recommended = 1
                        THEN OriginalAmount
                    END DESC,

                    VehicleName,
                    VendorId

            ) AS RowNumber

        FROM #FilteredResults

    ) P

    WHERE
        P.RowNumber > @StartRow
        AND P.RowNumber <= (@StartRow + @PageSize);

    SELECT
        VendorId,
        VehicleId,
        VehicleName,
        VehicleDetailsId,
        VehicleCategoryId,
        VehicleBrandId,
        VehicleImageURL,
        VehicleDescription,
        FuelType,
        DistanceInKm,
        DiscountPercentage,
        OriginalAmount,
        DiscountedAmount,
        GSTPercentage,
        GSTAmount,
        IncludedKilometres,
        PostLimitChargePerKM,
        Inclusions,
        AvailabilityStatus,
        AvailableFrom,
        TripMode,
        RateAlgorithm,
        YardId,
        YardName,
        YardToPickupKm,
        YardDistanceStatus,
        CustomerJourneyKm,
        TotalCalculatedKm,
        BilledDays,
        ExtraKm,
        ExtraKmAmount,
        AirportCharges,
        FinalFare

    FROM #Page

    ORDER BY RowNumber;

    ------------------------------------------------
    -- 9. Result Set 2: Pagination Information
    ------------------------------------------------

    SELECT
        @CurrentPage AS CurrentPage,
        @PageSize AS PageSize,
        @TotalRecords AS TotalRecords,
        @TotalPages AS TotalPages;

    ------------------------------------------------
    -- 10. Result Set 3 (Airport mode only): airport charge breakdown of the listed vehicles
    ------------------------------------------------

    IF @Mode = 'Airport'
    BEGIN
        SELECT
            VendorId,
            VehicleBrandId,
            ChargeName,
            Amount,
            SortOrder

        FROM
        (
            SELECT
                AC.VendorId,
                AC.VehicleBrandId,
                AC.ChargeName,
                AC.Amount,
                AC.SortOrder,

                ROW_NUMBER() OVER
                (
                    PARTITION BY AC.VendorId, AC.VehicleBrandId, AC.ChargeName
                    ORDER BY AC.SortOrder, AC.ChargeId
                ) AS RowNum

            FROM TaxhiBEBrandAirportCharge AC

            WHERE
                AC.AirportTerminalId = @AirportTerminalId
                AND AC.IsActive = 1
                AND EXISTS
                (
                    SELECT 1
                    FROM #Page PG
                    WHERE
                        PG.VendorId = AC.VendorId
                        AND PG.VehicleBrandId = AC.VehicleBrandId
                )

        ) X

        WHERE X.RowNum = 1

        ORDER BY VendorId, VehicleBrandId, SortOrder, ChargeName;
    END;

    ------------------------------------------------
    -- 11. Cleanup
    ------------------------------------------------

    DROP TABLE #Page;
    DROP TABLE #FilteredResults;

END;


GO

/* Active yards of one vendor (or of every vendor when @VendorId is NULL); the API resolves their road distances to the pickup. */
CREATE OR ALTER PROCEDURE [dbo].[USP_GetVendorYards]
(
    @VendorId INT = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Y.YardId,
        Y.VendorId,
        Y.YardName,
        Y.Latitude,
        Y.Longitude
    FROM TransportVendorYard Y
    WHERE
        Y.IsActive = 1
        AND (@VendorId IS NULL OR Y.VendorId = @VendorId)
    ORDER BY Y.VendorId, Y.YardId;
END
GO
