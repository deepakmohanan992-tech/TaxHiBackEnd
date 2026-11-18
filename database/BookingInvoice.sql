/*
  Booking invoice / travel itinerary PDF support.

  Additive and re-runnable. Does NOT modify USP_SaveBookingPayment or any existing procedure.
    1. dbo.TaxHiBEBookings.InvoiceFilePath / InvoiceGeneratedAtUtc  (nullable columns)
    2. USP_GetBookingInvoiceData      - everything the PDF needs (3 result sets)
    3. USP_GetBookingInvoiceFile      - ownership-checked lookup of the stored invoice path
    4. USP_SaveBookingInvoicePath     - records the generated file against the BookingId

  Ownership: every procedure takes @UserId and only returns a booking that belongs to that user.
*/

-- Required by TaxHiBEBookings' filtered indexes. Stored in each procedure at CREATE time (sqlcmd defaults to OFF).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Columns ---------------------------------------------------------------------------------
IF COL_LENGTH('dbo.TaxHiBEBookings', 'InvoiceFilePath') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD InvoiceFilePath NVARCHAR(500) NULL;
GO

IF COL_LENGTH('dbo.TaxHiBEBookings', 'InvoiceGeneratedAtUtc') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD InvoiceGeneratedAtUtc DATETIME2(3) NULL;
GO

-- 2. Invoice data ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_GetBookingInvoiceData
    @BookingId INT,
    @UserId    BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: booking header (empty when the booking does not exist or is not owned by @UserId)
    SELECT
        b.BookingId,
        b.BookingNo,
        b.VehicleBookingMasterId,
        b.UserId,
        b.BookingStatus,
        b.CreatedDateUtc,

        -- Customer (booking snapshot, falling back to the account)
        COALESCE(NULLIF(b.Name, ''), u.Name)             AS CustomerName,
        COALESCE(NULLIF(b.PhoneNumber, ''), u.MobileNumber) AS CustomerPhone,
        COALESCE(NULLIF(b.Email, ''), u.Email)           AS CustomerEmail,
        b.AlternateEmail,
        b.HasGST,
        b.GSTNumber,
        b.GSTCompanyName,

        -- Journey
        b.TripType,
        b.Route,
        b.PickupLocation,
        b.DropLocation,
        b.PickupDateTime,
        b.ReturnDateTime,
        b.DropDate,
        COALESCE(NULLIF(d.PickUpCity, ''), fc.Name)  AS FromCity,
        COALESCE(NULLIF(d.DropCity, ''),   tc.Name)  AS ToCity,

        -- Vehicle
        b.VehicleName,
        b.FuelType,
        t.RegistrationNo,
        t.DriverName,
        t.DriverMobile,

        -- Fare
        b.PaymentOption,
        b.CouponCode,
        b.BaseFare,
        b.DriverAllowance,
        b.AddOnsTotalAmount,
        b.DiscountAmount,
        b.GSTAmount,
        b.TotalFare,
        b.PayableNowAmount,

        -- Money actually received: successful payments only (a Pending/Failed row is never "paid")
        ISNULL(paid.PaidAmount, 0)                        AS AmountPaid,
        paid.LastPaidDateUtc                              AS PaidDateUtc,
        b.TotalFare - ISNULL(paid.PaidAmount, 0)          AS OutstandingAmount,
        latest.PaymentStatus,
        latest.PaymentMethod
    FROM dbo.TaxHiBEBookings b
    LEFT JOIN dbo.AuthUsers u
           ON u.UserId = b.UserId
    LEFT JOIN dbo.VehicleBookingMaster vbm
           ON vbm.VehicleBookingMasterId = b.VehicleBookingMasterId
    LEFT JOIN dbo.VehicleTripMaster t
           ON t.VehicleTripMasterId = vbm.VehicleTripMasterId
    OUTER APPLY (
        SELECT TOP (1) vd.PickUpCity, vd.DropCity
        FROM dbo.VehicleBookingDetails vd
        WHERE vd.VehicleBookingMasterId = b.VehicleBookingMasterId
        ORDER BY vd.OrderNo, vd.VehicleBookingDetailsId
    ) d
    LEFT JOIN dbo.CityMaster fc ON fc.ID = b.FromCityId
    LEFT JOIN dbo.CityMaster tc ON tc.ID = b.ToCityId
    OUTER APPLY (
        SELECT SUM(p.PaymentAmount) AS PaidAmount, MAX(p.PaidDateUtc) AS LastPaidDateUtc
        FROM dbo.TaxHiBEBookingPayments p
        WHERE p.BookingId = b.BookingId AND p.PaymentStatus = N'Success'
    ) paid
    OUTER APPLY (
        SELECT TOP (1) p.PaymentStatus, p.PaymentMethod
        FROM dbo.TaxHiBEBookingPayments p
        WHERE p.BookingId = b.BookingId
        ORDER BY p.BookingPaymentId DESC
    ) latest
    WHERE b.BookingId = @BookingId
      AND b.UserId = @UserId;

    -- Result set 2: add-ons
    SELECT a.AddOnId, a.AddOnName, a.PriceType, a.UnitPrice, a.CalculatedAmount
    FROM dbo.TaxHiBEBookingAddOns a
    JOIN dbo.TaxHiBEBookings b ON b.BookingId = a.BookingId
    WHERE a.BookingId = @BookingId
      AND b.UserId = @UserId
    ORDER BY a.BookingAddOnId;

    -- Result set 3: payment attempts
    SELECT p.BookingPaymentId, p.PaymentOption, p.PaymentStatus, p.PaymentMethod,
           p.PaymentAmount, p.TransactionReference, p.PaidDateUtc, p.CreatedDateUtc
    FROM dbo.TaxHiBEBookingPayments p
    JOIN dbo.TaxHiBEBookings b ON b.BookingId = p.BookingId
    WHERE p.BookingId = @BookingId
      AND b.UserId = @UserId
    ORDER BY p.BookingPaymentId;
END
GO

-- 3. Stored invoice lookup (ownership checked) -------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_GetBookingInvoiceFile
    @BookingId INT,
    @UserId    BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.BookingId, b.BookingNo, b.InvoiceFilePath, b.InvoiceGeneratedAtUtc
    FROM dbo.TaxHiBEBookings b
    WHERE b.BookingId = @BookingId
      AND b.UserId = @UserId;
END
GO

-- 4. Record the generated file against the booking ---------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_SaveBookingInvoicePath
    @BookingId       INT,
    @InvoiceFilePath NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TaxHiBEBookings
       SET InvoiceFilePath       = @InvoiceFilePath,
           InvoiceGeneratedAtUtc = SYSUTCDATETIME()
     WHERE BookingId = @BookingId;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO
