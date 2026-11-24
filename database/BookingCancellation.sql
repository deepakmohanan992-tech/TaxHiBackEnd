/*
  Booking cancellation support. Additive and re-runnable; changes no existing procedure.

    1. Cancellation / refund columns on dbo.TaxHiBEBookings (added only when missing).
    2. dbo.USP_GetBookingCancellationContext - read model used to validate ownership/status and to price the refund.
    3. dbo.sp_CancelBooking                  - atomic + idempotent: Pending/Confirmed -> Cancelled, stores reason,
                                               remarks, who/when and the refund decision. Only ONE caller can win.
                                               Refunds are NOT sent to a payment gateway: the accounts team refunds
                                               manually (RefundStatus = 'Pending') from the cancellation email.
    OTP is verified by the API (dbo.usp_VerifyOTP) before this procedure is ever called.

  The vehicle is released automatically: USP_SaveBookingPayment only treats Confirmed (and unexpired Pending)
  bookings as blocking, so a Cancelled booking frees the slot.

  ASSUMED SCHEMA (adjust if your database differs):
    dbo.TaxHiBEBookings:        BookingId, BookingNo, UserId, VendorId, BookingStatus, PickupDateTime,
                                VehicleBookingMasterId, ModifiedDateUtc
    dbo.TaxHiBEBookingPayments: BookingPaymentId, BookingId, PaymentAmount, PaymentStatus, PaymentGateway,
                                PaymentGatewayPaymentId
    dbo.VehicleBookingMaster:   VehicleBookingMasterId, VehicleTripMasterId, CancellationRemark
    The request's TripId is matched against VehicleBookingMaster.VehicleTripMasterId.
    BookingStatus 'Cancelled' is already used by USP_ConfirmBookingGatewayPayment.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Columns ------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.TaxHiBEBookings', 'CancellationReason') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD CancellationReason NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookings', 'CancellationRemarks') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD CancellationRemarks NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookings', 'CancelledBy') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD CancelledBy BIGINT NULL;
IF COL_LENGTH('dbo.TaxHiBEBookings', 'CancelledDateUtc') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD CancelledDateUtc DATETIME2(3) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookings', 'RefundAmount') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD RefundAmount DECIMAL(18,2) NULL;
-- NotApplicable | Pending (refund due, to be paid manually by the accounts team)
IF COL_LENGTH('dbo.TaxHiBEBookings', 'RefundStatus') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD RefundStatus VARCHAR(20) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookings', 'RefundUpdatedDateUtc') IS NULL
    ALTER TABLE dbo.TaxHiBEBookings ADD RefundUpdatedDateUtc DATETIME2(3) NULL;
GO

-- 2. Cancellation context -----------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_GetBookingCancellationContext
    @BookingId INT,
    @UserId    BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Empty when the booking does not exist or is not owned by @UserId (same answer for both, so ids cannot be probed).
    SELECT
        B.BookingId,
        B.BookingNo,
        B.UserId,
        B.VendorId,
        B.BookingStatus,
        B.PickupDateTime,
        VBM.VehicleTripMasterId                               AS TripId,
        P.BookingPaymentId,
        CAST(ISNULL(P.PaymentAmount, 0) AS DECIMAL(18,2))     AS AmountPaid,
        P.PaymentGateway,
        P.PaymentGatewayPaymentId
    FROM dbo.TaxHiBEBookings B
    LEFT JOIN dbo.VehicleBookingMaster VBM
           ON VBM.VehicleBookingMasterId = B.VehicleBookingMasterId
    OUTER APPLY (
        SELECT TOP (1) P1.BookingPaymentId, P1.PaymentAmount, P1.PaymentGateway, P1.PaymentGatewayPaymentId
        FROM dbo.TaxHiBEBookingPayments P1
        WHERE P1.BookingId = B.BookingId AND P1.PaymentStatus = 'Success'
        ORDER BY P1.BookingPaymentId DESC
    ) P
    WHERE B.BookingId = @BookingId
      AND B.UserId = @UserId;
END
GO

-- 3. Cancel -------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_CancelBooking
    @BookingId    INT,
    @UserId       BIGINT,
    @TripId       INT           = NULL,
    @Reason       NVARCHAR(200),
    @Remarks      NVARCHAR(500) = NULL,
    @RefundAmount DECIMAL(18,2) = 0,
    @RefundStatus VARCHAR(20)   = 'NotApplicable'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(),
            @Status VARCHAR(30), @OwnerId BIGINT, @VendorId INT, @BookingNo NVARCHAR(50),
            @VbmId INT, @BookingTripId INT, @Paid DECIMAL(18,2), @Outcome VARCHAR(30);

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @Status = B.BookingStatus, @OwnerId = B.UserId, @VendorId = B.VendorId, @BookingNo = B.BookingNo,
               @VbmId = B.VehicleBookingMasterId
        FROM dbo.TaxHiBEBookings B WITH (UPDLOCK, HOLDLOCK)
        WHERE B.BookingId = @BookingId;

        SELECT @BookingTripId = VehicleTripMasterId FROM dbo.VehicleBookingMaster WHERE VehicleBookingMasterId = @VbmId;

        IF @Status IS NULL OR @OwnerId <> @UserId
            SET @Outcome = 'NotFound';
        ELSE IF @TripId IS NOT NULL AND ISNULL(@BookingTripId, -1) <> @TripId
            SET @Outcome = 'TripMismatch';
        ELSE IF @Status NOT IN ('Pending', 'Confirmed')
            SET @Outcome = 'NotCancellable';
        ELSE
        BEGIN
            -- Never refund more than was actually collected.
            SELECT @Paid = ISNULL(SUM(PaymentAmount), 0)
            FROM dbo.TaxHiBEBookingPayments WHERE BookingId = @BookingId AND PaymentStatus = 'Success';

            IF @RefundAmount > @Paid SET @RefundAmount = @Paid;
            IF @RefundAmount <= 0 SELECT @RefundAmount = 0, @RefundStatus = 'NotApplicable';
            ELSE SET @RefundStatus = 'Pending';

            UPDATE dbo.TaxHiBEBookings
            SET BookingStatus       = 'Cancelled',
                CancellationReason  = @Reason,
                CancellationRemarks = @Remarks,
                CancelledBy         = @UserId,
                CancelledDateUtc    = @Now,
                RefundAmount        = @RefundAmount,
                RefundStatus        = @RefundStatus,
                RefundUpdatedDateUtc = @Now,
                ModifiedDateUtc     = @Now
            WHERE BookingId = @BookingId;

            UPDATE dbo.VehicleBookingMaster
            SET CancellationRemark = LEFT(CONCAT(@Reason, CASE WHEN @Remarks IS NULL THEN N'' ELSE N' - ' + @Remarks END), 500)
            WHERE VehicleBookingMasterId = @VbmId;

            SET @Outcome = 'Cancelled';
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT
        @Outcome       AS Outcome,
        @BookingId     AS BookingId,
        @BookingNo     AS BookingNo,
        @VendorId      AS VendorId,
        @Status        AS PreviousStatus,
        CASE WHEN @Outcome = 'Cancelled' THEN @RefundAmount ELSE CAST(0 AS DECIMAL(18,2)) END AS RefundAmount,
        CASE WHEN @Outcome = 'Cancelled' THEN @RefundStatus ELSE 'NotApplicable' END           AS RefundStatus,
        CASE WHEN @Outcome = 'Cancelled' THEN @Now END                                          AS CancelledDateUtc;
END
GO

