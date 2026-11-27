/*
  Booking hold release: a vehicle is booked only by a CONFIRMED (paid) booking or by an ACTIVE hold.

  Additive and re-runnable. Does not touch USP_SaveBookingPayment (it already runs in one transaction with
  XACT_ABORT, takes the per-vehicle lock, is idempotent on ClientRequestId and inserts the trip, guest, booking,
  details, add-ons and Pending payment row atomically) and does not change any table.

  State model (existing statuses only; no new enum values, so no CHECK constraint or consumer is affected)

    Booking Pending + HoldExpiresAtUtc in the future   vehicle HELD (blocks other customers)
    Booking Pending + hold lapsed / released           vehicle FREE; a late successful payment is re-validated
                                                       under the vehicle lock by USP_ConfirmBookingGatewayPayment
    Booking Confirmed                                  vehicle BOOKED (only after a verified successful payment)
    Booking Failed / Cancelled                         vehicle FREE

  This script
    1. USP_FailBookingGatewayPayment  (replaces the version in RazorpayPayment.sql)
         a failed attempt now also RELEASES the hold (Pending booking only; Success is never downgraded).
    2. USP_CancelBookingPayment       (new)
         the customer abandoned / cancelled the payment: payment 'Failed' ("cancelled"), hold released.
    3. USP_ExpireBookingHolds         (new, called by the API's background sweeper)
         Pending bookings whose hold lapsed more than @GraceMinutes ago and that were never paid become
         payment 'Failed' ("expired") + booking 'Failed'. The grace period keeps late gateway callbacks
         re-validatable instead of turning them into refunds.

  Transaction ownership: each procedure starts a transaction only when none is open (@@TRANCOUNT = 0) and only that
  owner commits / rolls back, so it can be called from inside a caller's transaction without a count mismatch.
  Compatible with database compatibility level 100 (no CONCAT / IIF / TRY_* / FORMAT / JSON functions).
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Failed attempt: record it and release the hold -----------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_FailBookingGatewayPayment
    @GatewayOrderId   VARCHAR(64),
    @GatewayPaymentId VARCHAR(64)   = NULL,
    @Reason           NVARCHAR(500) = NULL,
    @EventId          VARCHAR(100)  = NULL,
    @EventType        VARCHAR(60)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(),
            @OwnsTran BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END,
            @Outcome VARCHAR(30) = NULL, @PaymentId BIGINT = NULL, @BookingId INT = NULL, @PayStatus NVARCHAR(20);

    BEGIN TRY
        IF @OwnsTran = 1 BEGIN TRANSACTION;

        IF @EventId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TaxHiBEPaymentGatewayEvents WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId)
                SET @Outcome = 'DuplicateEvent';
            ELSE
                INSERT INTO dbo.TaxHiBEPaymentGatewayEvents (EventId, EventType, GatewayOrderId, GatewayPaymentId)
                VALUES (@EventId, @EventType, @GatewayOrderId, @GatewayPaymentId);
        END;

        SELECT @PaymentId = BookingPaymentId, @BookingId = BookingId, @PayStatus = PaymentStatus
        FROM dbo.TaxHiBEBookingPayments WITH (UPDLOCK, ROWLOCK)
        WHERE PaymentGatewayOrderId = @GatewayOrderId;

        IF @Outcome IS NULL
        BEGIN
            IF @PaymentId IS NULL
                SET @Outcome = 'OrderNotFound';
            ELSE IF @PayStatus IN ('Success', 'Refunded')
                SET @Outcome = 'Ignored';          -- a late/out-of-order failure never undoes a successful payment
            ELSE
            BEGIN
                UPDATE dbo.TaxHiBEBookingPayments
                SET PaymentStatus = 'Failed',
                    PaymentGateway = 'Razorpay',
                    PaymentGatewayPaymentId = ISNULL(@GatewayPaymentId, PaymentGatewayPaymentId),
                    FailureReason = @Reason
                WHERE BookingPaymentId = @PaymentId;

                -- Release the vehicle now. A success that still arrives on this order is re-validated before it confirms.
                UPDATE dbo.TaxHiBEBookings WITH (UPDLOCK, ROWLOCK)
                SET HoldExpiresAtUtc = @Now, ModifiedDateUtc = @Now
                WHERE BookingId = @BookingId
                  AND BookingStatus = 'Pending'
                  AND (HoldExpiresAtUtc IS NULL OR HoldExpiresAtUtc > @Now);

                SET @Outcome = 'Failed';
            END;
        END;

        IF @EventId IS NOT NULL
            UPDATE dbo.TaxHiBEPaymentGatewayEvents SET Outcome = @Outcome WHERE EventId = @EventId AND Outcome IS NULL;

        IF @OwnsTran = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnsTran = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;  -- the event row rolls back too, so a retry reprocesses
        THROW;
    END CATCH;

    SELECT @Outcome AS Outcome, ISNULL(B.BookingId, 0) AS BookingId, ISNULL(B.UserId, 0) AS UserId,
           B.BookingNo, B.BookingStatus, P.PaymentStatus,
           CAST(CASE WHEN P.PaymentStatus = 'Success' THEN P.PaymentAmount ELSE 0 END AS DECIMAL(18,2)) AS AmountPaid,
           ISNULL(B.BalancePayableAmount, 0) AS BalancePayableAmount
    FROM (SELECT 1 AS One) D
    LEFT JOIN dbo.TaxHiBEBookingPayments P ON P.BookingPaymentId = @PaymentId
    LEFT JOIN dbo.TaxHiBEBookings B ON B.BookingId = P.BookingId;
END;
GO

-- 2. Customer cancelled / abandoned the payment ---------------------------------------------------------
-- Outcome: Released | AlreadyReleased | AlreadyPaid | NotPayable | NotFound
CREATE OR ALTER PROCEDURE dbo.USP_CancelBookingPayment
    @BookingId INT,
    @UserId    BIGINT,
    @Reason    NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(),
            @OwnsTran BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END,
            @Outcome VARCHAR(30) = NULL, @PaymentId BIGINT = NULL, @PayStatus NVARCHAR(20),
            @OwnerId BIGINT, @Status NVARCHAR(30), @HoldExpires DATETIME2(3);

    BEGIN TRY
        IF @OwnsTran = 1 BEGIN TRANSACTION;

        SELECT @OwnerId = B.UserId, @Status = B.BookingStatus, @HoldExpires = B.HoldExpiresAtUtc
        FROM dbo.TaxHiBEBookings B WITH (UPDLOCK, ROWLOCK)
        WHERE B.BookingId = @BookingId;

        SELECT TOP (1) @PaymentId = P.BookingPaymentId, @PayStatus = P.PaymentStatus
        FROM dbo.TaxHiBEBookingPayments P WITH (UPDLOCK, ROWLOCK)
        WHERE P.BookingId = @BookingId
        ORDER BY P.BookingPaymentId DESC;

        IF @Status IS NULL OR @OwnerId <> @UserId           -- same answer for "missing" and "not yours"
            SET @Outcome = 'NotFound';
        ELSE IF @Status = 'Confirmed' OR @PayStatus IN ('Success', 'Refunded')
            SET @Outcome = 'AlreadyPaid';                   -- verified money is never undone by a client call
        ELSE IF @Status <> 'Pending'
            SET @Outcome = 'NotPayable';
        ELSE
        BEGIN
            IF @PayStatus <> 'Failed'
                UPDATE dbo.TaxHiBEBookingPayments
                SET PaymentStatus = 'Failed', FailureReason = ISNULL(@Reason, N'Payment cancelled by the customer')
                WHERE BookingPaymentId = @PaymentId;

            IF @HoldExpires IS NULL OR @HoldExpires > @Now
            BEGIN
                UPDATE dbo.TaxHiBEBookings SET HoldExpiresAtUtc = @Now, ModifiedDateUtc = @Now WHERE BookingId = @BookingId;
                SET @Outcome = 'Released';
            END
            ELSE
                SET @Outcome = 'AlreadyReleased';
        END;

        IF @OwnsTran = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnsTran = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT @Outcome AS Outcome, ISNULL(B.BookingId, 0) AS BookingId, ISNULL(B.UserId, 0) AS UserId,
           B.BookingNo, B.BookingStatus, P.PaymentStatus,
           CAST(CASE WHEN P.PaymentStatus = 'Success' THEN P.PaymentAmount ELSE 0 END AS DECIMAL(18,2)) AS AmountPaid,
           ISNULL(B.BalancePayableAmount, 0) AS BalancePayableAmount
    FROM (SELECT 1 AS One) D
    LEFT JOIN dbo.TaxHiBEBookings B ON B.BookingId = @BookingId AND @Outcome <> 'NotFound'   -- never echo another customer's booking
    LEFT JOIN dbo.TaxHiBEBookingPayments P ON P.BookingPaymentId = @PaymentId AND @Outcome <> 'NotFound';
END;
GO

-- 3. Sweep unpaid holds that lapsed ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_ExpireBookingHolds
    @GraceMinutes INT = 60,
    @BatchSize    INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(),
            @Cutoff DATETIME2(3),
            @OwnsTran BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END,
            @Expired INT = 0;

    IF @GraceMinutes < 0 SET @GraceMinutes = 0;
    IF @BatchSize < 1 SET @BatchSize = 200;
    SET @Cutoff = DATEADD(MINUTE, -@GraceMinutes, @Now);

    DECLARE @Ids TABLE (BookingId INT PRIMARY KEY);

    BEGIN TRY
        IF @OwnsTran = 1 BEGIN TRANSACTION;

        INSERT INTO @Ids (BookingId)
        SELECT TOP (@BatchSize) B.BookingId
        FROM dbo.TaxHiBEBookings B WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE B.BookingStatus = 'Pending'
          AND B.HoldExpiresAtUtc IS NOT NULL
          AND B.HoldExpiresAtUtc <= @Cutoff
          AND NOT EXISTS (SELECT 1 FROM dbo.TaxHiBEBookingPayments P WHERE P.BookingId = B.BookingId AND P.PaymentStatus IN ('Success', 'Refunded'))
        ORDER BY B.BookingId;

        UPDATE P
        SET PaymentStatus = 'Failed', FailureReason = N'Payment window expired'
        FROM dbo.TaxHiBEBookingPayments P
        INNER JOIN @Ids I ON I.BookingId = P.BookingId
        WHERE P.PaymentStatus = 'Pending';

        UPDATE B
        SET BookingStatus = 'Failed', ModifiedDateUtc = @Now
        FROM dbo.TaxHiBEBookings B
        INNER JOIN @Ids I ON I.BookingId = B.BookingId
        WHERE B.BookingStatus = 'Pending';

        SET @Expired = @@ROWCOUNT;

        IF @OwnsTran = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnsTran = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT @Expired AS ExpiredCount;
END;
GO
