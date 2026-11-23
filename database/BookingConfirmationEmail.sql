/*
  Booking confirmation email support. Additive and re-runnable; changes no existing procedure.

    1. USP_GetBookingConfirmationEmailDetails - one row with everything the email needs
                                                (customer, trip, vehicle, vendor Gmail credentials, CC addresses).
    2. dbo.TransportVendorAuditLog / dbo.SaveTransportVendorAuditLog - created ONLY if they do not already exist.

  ASSUMED SCHEMA (adjust if your database differs):
    dbo.TransportVendorMaster has the columns
        VendorId, VendorName, Website, GmailUserName, GmailAppPassword, ReservationStaffEmail, ContactPersonEmail
    dbo.SaveTransportVendorAuditLog takes (@VendorId, @UserId, @ActionName, @Details).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Email payload ------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_GetBookingConfirmationEmailDetails
    @BookingId INT,
    @UserId    BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Empty when the booking does not exist or is not owned by @UserId.
    SELECT
        b.BookingId,
        b.BookingNo,
        b.UserId,
        b.VendorId,

        COALESCE(NULLIF(b.Name, ''), u.Name)                AS CustomerName,
        COALESCE(NULLIF(b.Email, ''), u.Email)              AS CustomerEmail,
        COALESCE(NULLIF(b.PhoneNumber, ''), u.MobileNumber) AS CustomerPhone,

        b.TripType,
        b.PickupDateTime,
        b.PickupLocation,
        b.DropLocation,
        COALESCE(NULLIF(d.PickUpCity, ''), fc.Name)         AS PickupCity,
        COALESCE(NULLIF(d.DropCity, ''),   tc.Name)         AS DropCity,
        rd.GoogleDistanceKm                                 AS DistanceKm,
        CAST(rate.MinimumKM AS DECIMAL(18,2))               AS IncludedKilometres,
        CAST(rate.ExtraKMRate AS DECIMAL(18,2))             AS ExtraKmRate,

        b.VehicleName                                       AS CarType,
        b.InvoiceFilePath,

        v.VendorName,
        v.Website                                           AS VendorWebsite,
        v.GmailUserName,
        v.GmailAppPassword,
        v.ReservationStaffEmail,
        v.ContactPersonEmail
    FROM dbo.TaxHiBEBookings b
    LEFT JOIN dbo.AuthUsers u
           ON u.UserId = b.UserId
    LEFT JOIN dbo.TransportVendorMaster v
           ON v.VendorId = b.VendorId
    OUTER APPLY (
        SELECT TOP (1) vd.PickUpCity, vd.DropCity
        FROM dbo.VehicleBookingDetails vd
        WHERE vd.VehicleBookingMasterId = b.VehicleBookingMasterId
        ORDER BY vd.OrderNo, vd.VehicleBookingDetailsId
    ) d
    LEFT JOIN dbo.CityMaster fc ON fc.ID = b.FromCityId
    LEFT JOIN dbo.CityMaster tc ON tc.ID = b.ToCityId
    LEFT JOIN dbo.TaxHiBEBookingRouteDetails rd
           ON rd.BookingId = b.BookingId
    OUTER APPLY (
        SELECT TOP (1) RD2.MinimumKM, RD2.ExtraKMRate
        FROM dbo.TransportVendorRateDetails RD2
        WHERE RD2.VendorId = b.VendorId
          AND RD2.VehicleCategoryId = b.VehicleCategoryId
          AND RD2.IsActive = 1
          AND (   (RD2.ContractFromDate <= COALESCE(CAST(b.DropDate AS DATE), CAST(b.PickupDateTime AS DATE))
                   AND RD2.ContractToDate >= CAST(b.PickupDateTime AS DATE))
               OR RD2.ContractFromDate IS NULL)
        ORDER BY CASE WHEN RD2.ContractFromDate IS NOT NULL THEN 0 ELSE 1 END,
                 RD2.ContractFromDate DESC, RD2.RateDetailId DESC
    ) rate
    WHERE b.BookingId = @BookingId
      AND b.UserId = @UserId;
END
GO

-- 2. Audit log (only when the legacy objects are not present) ---------------------------------
IF OBJECT_ID('dbo.TransportVendorAuditLog', 'U') IS NULL
    CREATE TABLE dbo.TransportVendorAuditLog
    (
        AuditLogId  BIGINT IDENTITY(1,1) CONSTRAINT PK_TransportVendorAuditLog PRIMARY KEY,
        VendorId    INT            NULL,
        UserId      BIGINT         NULL,
        ActionName  NVARCHAR(100)  NOT NULL,
        Details     NVARCHAR(MAX)  NULL,
        CreatedDate DATETIME2(3)   NOT NULL CONSTRAINT DF_TransportVendorAuditLog_Created DEFAULT SYSUTCDATETIME()
    );
GO

IF OBJECT_ID('dbo.SaveTransportVendorAuditLog', 'P') IS NULL
    EXEC (N'
    CREATE PROCEDURE dbo.SaveTransportVendorAuditLog
        @VendorId   INT,
        @UserId     BIGINT,
        @ActionName NVARCHAR(100),
        @Details    NVARCHAR(MAX)
    AS
    BEGIN
        SET NOCOUNT ON;
        INSERT INTO dbo.TransportVendorAuditLog (VendorId, UserId, ActionName, Details)
        VALUES (@VendorId, @UserId, @ActionName, @Details);
    END');
GO
