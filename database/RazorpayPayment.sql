/*
  Razorpay Standard Checkout support.

  Additive and re-runnable. Does NOT modify USP_SaveBookingPayment: that procedure keeps owning booking creation
  (vehicle lock, availability check, ClientRequestId idempotency, add-ons, Pending hold + Pending payment row).
  This script only adds the gateway state that is written AFTER a payment is verified server-side.

    1. Gateway columns on dbo.TaxHiBEBookingPayments (added only when missing) + unique filtered indexes
       so one Razorpay order / payment id can never be attached to two payment rows.
    2. dbo.TaxHiBEPaymentGatewayEvents - one row per processed Razorpay webhook event (idempotency key).
    3. USP_GetBookingPaymentContext        - latest payment attempt of a booking / of a Razorpay order.
    4. USP_SetBookingPaymentGatewayOrder   - attaches the Razorpay order once (first writer wins).
    5. USP_ConfirmBookingGatewayPayment    - atomic + idempotent: payment Success, booking Confirmed.
    6. USP_FailBookingGatewayPayment       - records a failed attempt; never downgrades a Success payment.

  Pending holds / expiry: a payment that arrives after HoldExpiresAtUtc is still honoured when the vehicle is
  free (re-checked under the same per-vehicle lock USP_SaveBookingPayment uses). If another booking took the
  vehicle meanwhile, the money is recorded (payment Success), the booking is set to 'Failed' and the outcome
  is 'RefundRequired' so operations can refund it.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Columns / indexes -----------------------------------------------------------------------
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'PaymentGateway') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD PaymentGateway NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'PaymentGatewayOrderId') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD PaymentGatewayOrderId VARCHAR(64) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'PaymentGatewayPaymentId') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD PaymentGatewayPaymentId VARCHAR(64) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'PaymentMethod') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD PaymentMethod NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'FailureReason') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD FailureReason NVARCHAR(500) NULL;
-- TransactionReference and PaidDateUtc are already used by USP_GetBookingInvoiceData; added only if missing.
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'TransactionReference') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD TransactionReference NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.TaxHiBEBookingPayments', 'PaidDateUtc') IS NULL
    ALTER TABLE dbo.TaxHiBEBookingPayments ADD PaidDateUtc DATETIME2(3) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxHiBEBookingPayments_GatewayOrderId' AND object_id = OBJECT_ID('dbo.TaxHiBEBookingPayments'))
    CREATE UNIQUE INDEX UX_TaxHiBEBookingPayments_GatewayOrderId
        ON dbo.TaxHiBEBookingPayments (PaymentGatewayOrderId) WHERE PaymentGatewayOrderId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxHiBEBookingPayments_GatewayPaymentId' AND object_id = OBJECT_ID('dbo.TaxHiBEBookingPayments'))
    CREATE UNIQUE INDEX UX_TaxHiBEBookingPayments_GatewayPaymentId
        ON dbo.TaxHiBEBookingPayments (PaymentGatewayPaymentId) WHERE PaymentGatewayPaymentId IS NOT NULL;
GO

-- 2. Webhook / verification event log (idempotency) --------------------------------------------
IF OBJECT_ID('dbo.TaxHiBEPaymentGatewayEvents', 'U') IS NULL
    CREATE TABLE dbo.TaxHiBEPaymentGatewayEvents
    (
        EventId           VARCHAR(100) NOT NULL CONSTRAINT PK_TaxHiBEPaymentGatewayEvents PRIMARY KEY, -- X-Razorpay-Event-Id
        EventType         VARCHAR(60)  NULL,
        GatewayOrderId    VARCHAR(64)  NULL,
        GatewayPaymentId  VARCHAR(64)  NULL,
        Outcome           VARCHAR(30)  NULL,
        ReceivedDateUtc   DATETIME2(3) NOT NULL CONSTRAINT DF_TaxHiBEPaymentGatewayEvents_Received DEFAULT SYSUTCDATETIME()
    );
GO

-- 3. Payment context -------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_GetBookingPaymentContext
    @BookingId      INT          = NULL,
    @GatewayOrderId VARCHAR(64)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        B.BookingId,
        B.BookingNo,
        B.UserId,
        B.BookingStatus,
        B.HoldExpiresAtUtc,
        B.TotalFare,
        B.BalancePayableAmount,
        P.BookingPaymentId,
        P.PaymentStatus,
        P.PaymentAmount,
        P.PaymentGatewayOrderId,
        P.PaymentGatewayPaymentId
    FROM dbo.TaxHiBEBookingPayments P
    INNER JOIN dbo.TaxHiBEBookings B ON B.BookingId = P.BookingId
    WHERE (@GatewayOrderId IS NOT NULL AND P.PaymentGatewayOrderId = @GatewayOrderId)
       OR (@GatewayOrderId IS NULL AND @BookingId IS NOT NULL AND P.BookingId = @BookingId)
    ORDER BY P.BookingPaymentId DESC;
END;
GO

-- 4. Attach the Razorpay order (first writer wins) ----------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_SetBookingPaymentGatewayOrder
    @BookingPaymentId BIGINT,
    @GatewayOrderId   VARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TaxHiBEBookingPayments
    SET PaymentGateway = 'Razorpay',
        PaymentGatewayOrderId = @GatewayOrderId
    WHERE BookingPaymentId = @BookingPaymentId
      AND PaymentGatewayOrderId IS NULL
      AND PaymentStatus NOT IN ('Success', 'Refunded');

    SELECT PaymentGatewayOrderId
    FROM dbo.TaxHiBEBookingPayments
    WHERE BookingPaymentId = @BookingPaymentId;
END;
GO

-- 5. Confirm a verified payment ---------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.USP_ConfirmBookingGatewayPayment
    @GatewayOrderId   VARCHAR(64),
    @GatewayPaymentId VARCHAR(64),
    @AmountPaise      BIGINT        = NULL,   -- supplied by webhooks; the checkout signature does not carry an amount
    @PaymentMethod    NVARCHAR(50)  = NULL,
    @EventId          VARCHAR(100)  = NULL,   -- webhook event id; NULL for the checkout verification call
    @EventType        VARCHAR(60)   = NULL,
    @HoldMinutes      INT           = 15,
    @LockTimeoutMs    INT           = 10000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
        @Now DATETIME2(3) = SYSUTCDATETIME(),
        @Outcome VARCHAR(30) = NULL,
        @PaymentId BIGINT = NULL,
        @BookingId INT = NULL,
        @PayStatus NVARCHAR(20),
        @PayAmount DECIMAL(18,2),
        @StoredPaymentId VARCHAR(64),
        @BookingStatus NVARCHAR(30),
        @HoldExpires DATETIME2(3),
        @VehicleDetailsId INT,
        @VendorId INT,
        @ReqStart DATE,
        @ReqEnd DATE;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Webhook idempotency: the event id is the key; a redelivery never reprocesses.
        IF @EventId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TaxHiBEPaymentGatewayEvents WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId)
                SET @Outcome = 'DuplicateEvent';
            ELSE
                INSERT INTO dbo.TaxHiBEPaymentGatewayEvents (EventId, EventType, GatewayOrderId, GatewayPaymentId)
                VALUES (@EventId, @EventType, @GatewayOrderId, @GatewayPaymentId);
        END;

        SELECT @PaymentId = BookingPaymentId, @BookingId = BookingId, @PayStatus = PaymentStatus,
               @PayAmount = PaymentAmount, @StoredPaymentId = PaymentGatewayPaymentId
        FROM dbo.TaxHiBEBookingPayments WITH (UPDLOCK, ROWLOCK)
        WHERE PaymentGatewayOrderId = @GatewayOrderId;

        IF @Outcome IS NULL
        BEGIN
            IF @PaymentId IS NULL
                SET @Outcome = 'OrderNotFound';
            ELSE IF @PayStatus = 'Success'
                SET @Outcome = CASE WHEN @StoredPaymentId IS NOT NULL AND @StoredPaymentId <> @GatewayPaymentId
                                    THEN 'PaymentIdConflict' ELSE 'AlreadyProcessed' END;
            ELSE IF @PayStatus = 'Refunded'
                SET @Outcome = 'Ignored';
            ELSE IF @AmountPaise IS NOT NULL AND @AmountPaise <> CAST(ROUND(@PayAmount * 100, 0) AS BIGINT)
                SET @Outcome = 'AmountMismatch';
        END;

        IF @Outcome IS NULL
        BEGIN
            SELECT @BookingStatus = B.BookingStatus, @HoldExpires = B.HoldExpiresAtUtc,
                   @VehicleDetailsId = B.VehicleDetailsId, @VendorId = B.VendorId,
                   @ReqStart = CAST(B.PickupDateTime AS DATE),
                   @ReqEnd = ISNULL(B.DropDate, CAST(B.PickupDateTime AS DATE))
            FROM dbo.TaxHiBEBookings B WITH (UPDLOCK, ROWLOCK)
            WHERE B.BookingId = @BookingId;

            IF @BookingStatus IN ('Cancelled', 'Failed')
                SET @Outcome = 'RefundRequired';   -- money received for a booking that is no longer active
            ELSE IF @BookingStatus = 'Pending' AND (@HoldExpires IS NULL OR @HoldExpires <= @Now)
            BEGIN
                -- Hold lapsed before the payment arrived: confirm only if nobody else took the vehicle.
                EXEC dbo.USP_AcquireVehicleBookingLock @VehicleDetailsId = @VehicleDetailsId, @LockTimeoutMs = @LockTimeoutMs;

                IF EXISTS
                (
                    SELECT 1 FROM dbo.VehicleTripMaster T
                    WHERE T.VehicleDetailsId = @VehicleDetailsId AND T.VendorId = @VendorId
                      AND T.VehicleTripStatusId IN (1, 2)
                      AND T.FromDate IS NOT NULL AND T.FromDate <= @ReqEnd AND ISNULL(T.ToDate, T.FromDate) >= @ReqStart
                )
                OR EXISTS
                (
                    SELECT 1 FROM dbo.TaxHiBEBookings O
                    WHERE O.BookingId <> @BookingId
                      AND O.VehicleDetailsId = @VehicleDetailsId AND O.VendorId = @VendorId
                      AND O.PickupDateTime IS NOT NULL
                      AND (O.BookingStatus = 'Confirmed' OR (O.BookingStatus = 'Pending' AND O.HoldExpiresAtUtc > @Now))
                      AND CAST(O.PickupDateTime AS DATE) <= @ReqEnd
                      AND ISNULL(O.DropDate, CAST(O.PickupDateTime AS DATE)) >= @ReqStart
                )
                BEGIN
                    UPDATE dbo.TaxHiBEBookings SET BookingStatus = 'Failed', ModifiedDateUtc = @Now WHERE BookingId = @BookingId;
                    SET @Outcome = 'RefundRequired';
                END;
            END;
        END;

        -- Record the money and (unless the vehicle was lost) confirm the booking.
        IF @Outcome IS NULL OR @Outcome = 'RefundRequired'
        BEGIN
            UPDATE dbo.TaxHiBEBookingPayments
            SET PaymentStatus = 'Success',
                PaymentGateway = 'Razorpay',
                PaymentGatewayPaymentId = @GatewayPaymentId,
                TransactionReference = @GatewayPaymentId,
                PaymentMethod = ISNULL(@PaymentMethod, PaymentMethod),
                FailureReason = NULL,
                PaidDateUtc = @Now
            WHERE BookingPaymentId = @PaymentId;

            IF @Outcome IS NULL
            BEGIN
                UPDATE dbo.TaxHiBEBookings
                SET BookingStatus = 'Confirmed', ModifiedDateUtc = @Now
                WHERE BookingId = @BookingId AND BookingStatus IN ('Pending', 'Confirmed');
                SET @Outcome = 'Confirmed';
            END;
        END;

        IF @EventId IS NOT NULL
            UPDATE dbo.TaxHiBEPaymentGatewayEvents SET Outcome = @Outcome WHERE EventId = @EventId AND Outcome IS NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;  -- the event row rolls back too, so a Razorpay retry reprocesses
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

-- 6. Record a failed attempt ---------------------------------------------------------------------
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

    DECLARE @Outcome VARCHAR(30) = NULL, @PaymentId BIGINT = NULL, @PayStatus NVARCHAR(20);

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @EventId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TaxHiBEPaymentGatewayEvents WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId)
                SET @Outcome = 'DuplicateEvent';
            ELSE
                INSERT INTO dbo.TaxHiBEPaymentGatewayEvents (EventId, EventType, GatewayOrderId, GatewayPaymentId)
                VALUES (@EventId, @EventType, @GatewayOrderId, @GatewayPaymentId);
        END;

        SELECT @PaymentId = BookingPaymentId, @PayStatus = PaymentStatus
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
                -- The booking hold is left alone so the customer can retry on the same order until it expires.
                UPDATE dbo.TaxHiBEBookingPayments
                SET PaymentStatus = 'Failed',
                    PaymentGateway = 'Razorpay',
                    PaymentGatewayPaymentId = ISNULL(@GatewayPaymentId, PaymentGatewayPaymentId),
                    FailureReason = @Reason
                WHERE BookingPaymentId = @PaymentId;
                SET @Outcome = 'Failed';
            END;
        END;

        IF @EventId IS NOT NULL
            UPDATE dbo.TaxHiBEPaymentGatewayEvents SET Outcome = @Outcome WHERE EventId = @EventId AND Outcome IS NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
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
