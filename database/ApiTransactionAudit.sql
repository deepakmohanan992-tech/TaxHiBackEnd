/*
  Centralized API transaction audit.

  Additive and re-runnable; creates only new objects. Compatible with database compatibility level 100
  (no JSON / CONCAT / IIF / TRY_* functions, no DROP IF EXISTS, no columnstore).

    dbo.ApiTransactionAudit          one row per API request (success and failure)
    dbo.USP_InsertApiTransactionAudit  insert used by the API's background audit writer

  Sensitive data is redacted by the API BEFORE it reaches this table (passwords, OTPs, tokens, signatures, payment
  secrets, card data; e-mail / phone are masked). The payload columns hold the redacted, size-capped JSON only.
  An audit write failure never affects the API response (the writer swallows and logs it).
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.ApiTransactionAudit', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiTransactionAudit
    (
        AuditId              BIGINT         IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiTransactionAudit PRIMARY KEY CLUSTERED,

        -- Request
        CorrelationId        VARCHAR(64)    NOT NULL,
        RequestId            VARCHAR(64)    NULL,
        HttpMethod           VARCHAR(10)    NOT NULL,
        ApiEndpoint          NVARCHAR(300)  NOT NULL,   -- route path without query string
        ControllerName       NVARCHAR(100)  NULL,
        ActionName           NVARCHAR(100)  NULL,
        RequestUrl           NVARCHAR(1000) NULL,       -- path + redacted query string
        RequestPayload       NVARCHAR(MAX)  NULL,       -- redacted, size-capped
        RequestHeaders       NVARCHAR(2000) NULL,       -- safe headers only
        UserId               BIGINT         NULL,
        VendorId             INT            NULL,
        ClientIpAddress      VARCHAR(45)    NULL,
        UserAgent            NVARCHAR(500)  NULL,
        RequestReceivedAt    DATETIME2(3)   NOT NULL,

        -- Response
        HttpStatusCode       SMALLINT       NULL,
        ResponsePayload      NVARCHAR(MAX)  NULL,       -- redacted, size-capped
        ResponseReceivedAt   DATETIME2(3)   NULL,
        ExecutionDurationMs  INT            NULL,
        ApiStatus            VARCHAR(10)    NOT NULL,   -- Success | Failed | Timeout

        -- Error
        ErrorCode            VARCHAR(100)   NULL,
        ErrorMessage         NVARCHAR(1000) NULL,
        ExceptionType        VARCHAR(300)   NULL,
        ExceptionDetails     NVARCHAR(4000) NULL,
        StackTrace           NVARCHAR(MAX)  NULL,

        -- Metadata
        CreatedAt            DATETIME2(3)   NOT NULL CONSTRAINT DF_ApiTransactionAudit_CreatedAt DEFAULT SYSUTCDATETIME(),
        Environment          VARCHAR(30)    NULL,
        IsActive             BIT            NOT NULL CONSTRAINT DF_ApiTransactionAudit_IsActive DEFAULT 1,

        CONSTRAINT CK_ApiTransactionAudit_ApiStatus CHECK (ApiStatus IN ('Success', 'Failed', 'Timeout'))
    );
END
GO

-- Indexes (AuditId is the clustered primary key) -------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiTransactionAudit_CorrelationId' AND object_id = OBJECT_ID('dbo.ApiTransactionAudit'))
    CREATE NONCLUSTERED INDEX IX_ApiTransactionAudit_CorrelationId
        ON dbo.ApiTransactionAudit (CorrelationId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiTransactionAudit_UserId' AND object_id = OBJECT_ID('dbo.ApiTransactionAudit'))
    CREATE NONCLUSTERED INDEX IX_ApiTransactionAudit_UserId
        ON dbo.ApiTransactionAudit (UserId, RequestReceivedAt DESC) WHERE UserId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiTransactionAudit_RequestReceivedAt' AND object_id = OBJECT_ID('dbo.ApiTransactionAudit'))
    CREATE NONCLUSTERED INDEX IX_ApiTransactionAudit_RequestReceivedAt
        ON dbo.ApiTransactionAudit (RequestReceivedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiTransactionAudit_ApiEndpoint' AND object_id = OBJECT_ID('dbo.ApiTransactionAudit'))
    CREATE NONCLUSTERED INDEX IX_ApiTransactionAudit_ApiEndpoint
        ON dbo.ApiTransactionAudit (ApiEndpoint, RequestReceivedAt DESC);

-- Failures are the rows developers look for: a small index on the non-success statuses.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiTransactionAudit_ApiStatus' AND object_id = OBJECT_ID('dbo.ApiTransactionAudit'))
    CREATE NONCLUSTERED INDEX IX_ApiTransactionAudit_ApiStatus
        ON dbo.ApiTransactionAudit (ApiStatus, RequestReceivedAt DESC) INCLUDE (ApiEndpoint, HttpStatusCode, ErrorCode)
        WHERE ApiStatus <> 'Success';
GO

CREATE OR ALTER PROCEDURE dbo.USP_InsertApiTransactionAudit
    @CorrelationId       VARCHAR(64),
    @RequestId           VARCHAR(64)    = NULL,
    @HttpMethod          VARCHAR(10),
    @ApiEndpoint         NVARCHAR(300),
    @ControllerName      NVARCHAR(100)  = NULL,
    @ActionName          NVARCHAR(100)  = NULL,
    @RequestUrl          NVARCHAR(1000) = NULL,
    @RequestPayload      NVARCHAR(MAX)  = NULL,
    @RequestHeaders      NVARCHAR(2000) = NULL,
    @UserId              BIGINT         = NULL,
    @VendorId            INT            = NULL,
    @ClientIpAddress     VARCHAR(45)    = NULL,
    @UserAgent           NVARCHAR(500)  = NULL,
    @RequestReceivedAt   DATETIME2(3),
    @HttpStatusCode      SMALLINT       = NULL,
    @ResponsePayload     NVARCHAR(MAX)  = NULL,
    @ResponseReceivedAt  DATETIME2(3)   = NULL,
    @ExecutionDurationMs INT            = NULL,
    @ApiStatus           VARCHAR(10),
    @ErrorCode           VARCHAR(100)   = NULL,
    @ErrorMessage        NVARCHAR(1000) = NULL,
    @ExceptionType       VARCHAR(300)   = NULL,
    @ExceptionDetails    NVARCHAR(4000) = NULL,
    @StackTrace          NVARCHAR(MAX)  = NULL,
    @Environment         VARCHAR(30)    = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ApiTransactionAudit
    (
        CorrelationId, RequestId, HttpMethod, ApiEndpoint, ControllerName, ActionName, RequestUrl, RequestPayload,
        RequestHeaders, UserId, VendorId, ClientIpAddress, UserAgent, RequestReceivedAt,
        HttpStatusCode, ResponsePayload, ResponseReceivedAt, ExecutionDurationMs, ApiStatus,
        ErrorCode, ErrorMessage, ExceptionType, ExceptionDetails, StackTrace, Environment
    )
    VALUES
    (
        @CorrelationId, @RequestId, @HttpMethod, @ApiEndpoint, @ControllerName, @ActionName, @RequestUrl, @RequestPayload,
        @RequestHeaders, @UserId, @VendorId, @ClientIpAddress, @UserAgent, @RequestReceivedAt,
        @HttpStatusCode, @ResponsePayload, @ResponseReceivedAt, @ExecutionDurationMs, @ApiStatus,
        @ErrorCode, @ErrorMessage, @ExceptionType, @ExceptionDetails, @StackTrace, @Environment
    );
END
GO
