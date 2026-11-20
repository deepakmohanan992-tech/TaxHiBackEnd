/*
  Google route details per booking.

  Additive and re-runnable. Does NOT modify dbo.TaxHiBEBookings, USP_SaveBookingPayment or any existing procedure.
  The Google distance lives only here; the billing distance used for pricing is untouched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.TaxHiBEBookingRouteDetails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TaxHiBEBookingRouteDetails
    (
        BookingId             INT           NOT NULL CONSTRAINT PK_TaxHiBEBookingRouteDetails PRIMARY KEY,
        PickupPlaceId         NVARCHAR(300) NULL,
        PickupLatitude        DECIMAL(9,6)  NOT NULL,
        PickupLongitude       DECIMAL(9,6)  NOT NULL,
        DropPlaceId           NVARCHAR(300) NULL,
        DropLatitude          DECIMAL(9,6)  NOT NULL,
        DropLongitude         DECIMAL(9,6)  NOT NULL,
        GoogleDistanceKm      DECIMAL(10,2) NOT NULL,
        GoogleDurationMinutes INT           NOT NULL,
        EncodedPolyline       NVARCHAR(MAX) NULL,
        CreatedAtUtc          DATETIME2(3)  NOT NULL CONSTRAINT DF_TaxHiBEBookingRouteDetails_Created DEFAULT SYSUTCDATETIME(),
        ModifiedAtUtc         DATETIME2(3)  NOT NULL CONSTRAINT DF_TaxHiBEBookingRouteDetails_Modified DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_TaxHiBEBookingRouteDetails_Booking FOREIGN KEY (BookingId) REFERENCES dbo.TaxHiBEBookings (BookingId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_SaveBookingRouteDetails
    @BookingId             INT,
    @PickupPlaceId         NVARCHAR(300) = NULL,
    @PickupLatitude        DECIMAL(9,6),
    @PickupLongitude       DECIMAL(9,6),
    @DropPlaceId           NVARCHAR(300) = NULL,
    @DropLatitude          DECIMAL(9,6),
    @DropLongitude         DECIMAL(9,6),
    @GoogleDistanceKm      DECIMAL(10,2),
    @GoogleDurationMinutes INT,
    @EncodedPolyline       NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.TaxHiBEBookings WHERE BookingId = @BookingId)
        THROW 50010, 'Booking not found.', 1;

    UPDATE dbo.TaxHiBEBookingRouteDetails
    SET PickupPlaceId = @PickupPlaceId, PickupLatitude = @PickupLatitude, PickupLongitude = @PickupLongitude,
        DropPlaceId = @DropPlaceId, DropLatitude = @DropLatitude, DropLongitude = @DropLongitude,
        GoogleDistanceKm = @GoogleDistanceKm, GoogleDurationMinutes = @GoogleDurationMinutes,
        EncodedPolyline = @EncodedPolyline, ModifiedAtUtc = SYSUTCDATETIME()
    WHERE BookingId = @BookingId;

    IF @@ROWCOUNT = 0
    BEGIN TRY
        INSERT dbo.TaxHiBEBookingRouteDetails
            (BookingId, PickupPlaceId, PickupLatitude, PickupLongitude, DropPlaceId, DropLatitude, DropLongitude,
             GoogleDistanceKm, GoogleDurationMinutes, EncodedPolyline)
        VALUES
            (@BookingId, @PickupPlaceId, @PickupLatitude, @PickupLongitude, @DropPlaceId, @DropLatitude, @DropLongitude,
             @GoogleDistanceKm, @GoogleDurationMinutes, @EncodedPolyline);
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW; -- concurrent insert for the same booking: the other writer won
    END CATCH
END
GO
