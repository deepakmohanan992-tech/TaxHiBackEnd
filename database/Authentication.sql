/* Execute this script against the application database after reviewing existing booking schema. */
SET XACT_ABORT ON;
GO

/* Remove legacy authorization-code objects if the earlier optional integration was deployed. */
IF OBJECT_ID('dbo.usp_ExchangeThirdPartyAuthorizationCode', 'P') IS NOT NULL EXEC('DROP PROCEDURE dbo.usp_ExchangeThirdPartyAuthorizationCode');
IF OBJECT_ID('dbo.usp_CreateThirdPartyAuthorizationCode', 'P') IS NOT NULL EXEC('DROP PROCEDURE dbo.usp_CreateThirdPartyAuthorizationCode');
IF OBJECT_ID('dbo.ThirdPartyAuthorizationCodes', 'U') IS NOT NULL EXEC('DROP TABLE dbo.ThirdPartyAuthorizationCodes');
IF OBJECT_ID('dbo.ThirdPartyClients', 'U') IS NOT NULL EXEC('DROP TABLE dbo.ThirdPartyClients');
GO

IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.Users
	(
		UserId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
		MobileNumber NVARCHAR(20) NOT NULL CONSTRAINT UQ_Users_MobileNumber UNIQUE,
		Name NVARCHAR(200) NULL,
		Email NVARCHAR(320) NULL,
		IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
		CreatedDate DATETIME2(3) NOT NULL CONSTRAINT DF_Users_CreatedDate DEFAULT SYSUTCDATETIME(),
		ModifiedDate DATETIME2(3) NOT NULL CONSTRAINT DF_Users_ModifiedDate DEFAULT SYSUTCDATETIME()
	);
END;
GO
IF OBJECT_ID('dbo.OTPVerifications', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.OTPVerifications
	(
		OTPVerificationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OTPVerifications PRIMARY KEY,
		MobileNumber NVARCHAR(20) NOT NULL,
		Purpose NVARCHAR(100) NOT NULL,
		OTPHash NVARCHAR(128) NOT NULL,
		ExpiresUtc DATETIME2(3) NOT NULL,
		Attempts INT NOT NULL CONSTRAINT DF_OTP_Attempts DEFAULT 0,
		MaxAttempts INT NOT NULL,
		IsUsed BIT NOT NULL CONSTRAINT DF_OTP_IsUsed DEFAULT 0,
		CreatedDate DATETIME2(3) NOT NULL CONSTRAINT DF_OTP_CreatedDate DEFAULT SYSUTCDATETIME(),
		UsedDate DATETIME2(3) NULL
	);
	CREATE INDEX IX_OTP_MobilePurposeCreated ON dbo.OTPVerifications(MobileNumber, Purpose, CreatedDate DESC);
END;
GO
IF OBJECT_ID('dbo.RefreshTokens', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.RefreshTokens
	(
		RefreshTokenId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
		UserId BIGINT NOT NULL CONSTRAINT FK_RefreshTokens_Users REFERENCES dbo.Users(UserId),
		TokenHash NVARCHAR(128) NOT NULL CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE,
		ExpiresUtc DATETIME2(3) NOT NULL,
		RevokedUtc DATETIME2(3) NULL,
		CreatedDate DATETIME2(3) NOT NULL CONSTRAINT DF_Refresh_CreatedDate DEFAULT SYSUTCDATETIME()
	);
	CREATE INDEX IX_RefreshTokens_UserId ON dbo.RefreshTokens(UserId, ExpiresUtc);
END;
GO
IF OBJECT_ID('dbo.Bookings', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.Bookings
	(
		BookingId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
		UserId BIGINT NULL CONSTRAINT FK_Bookings_Users REFERENCES dbo.Users(UserId),
		BookingReference NVARCHAR(40) NOT NULL CONSTRAINT UQ_Bookings_Reference UNIQUE,
		BookingSource NVARCHAR(20) NOT NULL CONSTRAINT DF_Bookings_Source DEFAULT 'Registered',
		GuestName NVARCHAR(200) NULL,
		MobileNumber NVARCHAR(20) NOT NULL,
		Email NVARCHAR(320) NULL,
		Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Bookings_Status DEFAULT 'Confirmed',
		BookingDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Bookings_Date DEFAULT SYSUTCDATETIME(),
		ModifiedDate DATETIME2(3) NOT NULL CONSTRAINT DF_Bookings_Modified DEFAULT SYSUTCDATETIME()
	);
	CREATE INDEX IX_Bookings_GuestLookup ON dbo.Bookings(BookingReference, MobileNumber, BookingSource);
END;
GO
IF OBJECT_ID('dbo.BookingCancellationAudit', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.BookingCancellationAudit
	(
		BookingCancellationAuditId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingCancellationAudit PRIMARY KEY,
		BookingId BIGINT NOT NULL CONSTRAINT FK_BookingCancellationAudit_Bookings REFERENCES dbo.Bookings(BookingId),
		CancellationSource NVARCHAR(30) NOT NULL,
		CancelledDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_BookingCancellationAudit_Date DEFAULT SYSUTCDATETIME(),
		MobileNumber NVARCHAR(20) NOT NULL
	);
END;
GO
IF OBJECT_ID('dbo.BookingAccessTokens', 'U') IS NULL
BEGIN
	CREATE TABLE dbo.BookingAccessTokens
	(
		BookingAccessTokenId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingAccessTokens PRIMARY KEY,
		BookingId BIGINT NOT NULL CONSTRAINT FK_BookingAccessTokens_Bookings REFERENCES dbo.Bookings(BookingId),
		TokenHash NVARCHAR(128) NOT NULL CONSTRAINT UQ_BookingAccessTokens_TokenHash UNIQUE,
		ExpiresUtc DATETIME2(3) NOT NULL,
		CreatedDate DATETIME2(3) NOT NULL CONSTRAINT DF_BookingAccess_Created DEFAULT SYSUTCDATETIME(),
		RevokedUtc DATETIME2(3) NULL
	);
	CREATE INDEX IX_BookingAccessTokens_Booking ON dbo.BookingAccessTokens(BookingId, ExpiresUtc);
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateOTP
	@MobileNumber NVARCHAR(20), @Purpose NVARCHAR(100), @OtpHash NVARCHAR(128), @ExpiresUtc DATETIME2(3), @MaxAttempts INT, @CooldownSeconds INT
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;
	DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
	DECLARE @last DATETIME2(3);

	BEGIN TRAN;
	SELECT TOP (1) @last = CreatedDate
	FROM dbo.OTPVerifications WITH (UPDLOCK, HOLDLOCK)
	WHERE MobileNumber = @MobileNumber AND Purpose = @Purpose
	ORDER BY CreatedDate DESC;

	IF @last IS NOT NULL AND DATEDIFF(SECOND, @last, @now) < @CooldownSeconds
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Accepted,
			DATEADD(SECOND, @CooldownSeconds - DATEDIFF(SECOND, @last, @now), @now) ExpiresUtc,
			@CooldownSeconds - DATEDIFF(SECOND, @last, @now) RetryAfterSeconds;
		RETURN;
	END;

	UPDATE dbo.OTPVerifications
	SET IsUsed = 1, UsedDate = @now
	WHERE MobileNumber = @MobileNumber AND Purpose = @Purpose AND IsUsed = 0;

	INSERT dbo.OTPVerifications(MobileNumber, Purpose, OTPHash, ExpiresUtc, MaxAttempts)
	VALUES(@MobileNumber, @Purpose, @OtpHash, @ExpiresUtc, @MaxAttempts);
	COMMIT;

	SELECT CAST(1 AS BIT) Accepted, @ExpiresUtc ExpiresUtc, 0 RetryAfterSeconds;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_VerifyOTP @MobileNumber NVARCHAR(20), @Purpose NVARCHAR(100), @OtpHash NVARCHAR(128)
AS
BEGIN
	SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
	DECLARE @id BIGINT, @attempts INT, @max INT, @valid BIT=0, @failure NVARCHAR(50)='OTP_INVALID';
	SELECT TOP 1 @id=OTPVerificationId,@attempts=Attempts,@max=MaxAttempts FROM dbo.OTPVerifications WITH (UPDLOCK,ROWLOCK) WHERE MobileNumber=@MobileNumber AND Purpose=@Purpose AND IsUsed=0 ORDER BY CreatedDate DESC;
	IF @id IS NULL SET @failure='OTP_NOT_FOUND';
	ELSE IF @attempts >= @max SET @failure='OTP_MAX_ATTEMPTS';
	ELSE IF EXISTS(SELECT 1 FROM dbo.OTPVerifications WHERE OTPVerificationId=@id AND ExpiresUtc<=SYSUTCDATETIME()) SET @failure='OTP_EXPIRED';
	ELSE IF EXISTS(SELECT 1 FROM dbo.OTPVerifications WHERE OTPVerificationId=@id AND OTPHash=@OtpHash)
	BEGIN UPDATE dbo.OTPVerifications SET IsUsed=1,UsedDate=SYSUTCDATETIME() WHERE OTPVerificationId=@id; SET @valid=1; SET @failure=''; END
	ELSE BEGIN UPDATE dbo.OTPVerifications SET Attempts=Attempts+1 WHERE OTPVerificationId=@id; SET @failure='OTP_INVALID'; END;
	COMMIT;
	SELECT @valid Success,@failure FailureCode,CAST(NULL AS BIGINT) UserId,CAST(NULL AS NVARCHAR(200)) Name,CAST(NULL AS NVARCHAR(320)) Email;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RegisterOrGetUser @MobileNumber NVARCHAR(20)
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;
	BEGIN TRAN;
	IF NOT EXISTS
	(
		SELECT 1
		FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
		WHERE MobileNumber = @MobileNumber
	)
	BEGIN
		INSERT dbo.Users(MobileNumber) VALUES(@MobileNumber);
	END;
	COMMIT;
	SELECT UserId, MobileNumber, Name, Email
	FROM dbo.Users
	WHERE MobileNumber = @MobileNumber AND IsActive = 1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetUserById @UserId BIGINT AS BEGIN SET NOCOUNT ON; SELECT UserId,MobileNumber,Name,Email FROM dbo.Users WHERE UserId=@UserId AND IsActive=1; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SaveRefreshToken @UserId BIGINT,@TokenHash NVARCHAR(128),@ExpiresUtc DATETIME2(3) AS BEGIN SET NOCOUNT ON; INSERT dbo.RefreshTokens(UserId,TokenHash,ExpiresUtc) VALUES(@UserId,@TokenHash,@ExpiresUtc); END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ValidateRefreshToken @TokenHash NVARCHAR(128) AS BEGIN SET NOCOUNT ON; SELECT u.UserId,u.MobileNumber,u.Name,u.Email FROM dbo.RefreshTokens r JOIN dbo.Users u ON u.UserId=r.UserId WHERE r.TokenHash=@TokenHash AND r.RevokedUtc IS NULL AND r.ExpiresUtc>SYSUTCDATETIME() AND u.IsActive=1; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetRefreshTokenStatus @TokenHash NVARCHAR(128) AS BEGIN SET NOCOUNT ON; SELECT TOP 1 CAST(CASE WHEN RevokedUtc IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsRevoked, CAST(CASE WHEN ExpiresUtc<=SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsExpired FROM dbo.RefreshTokens WHERE TokenHash=@TokenHash; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RevokeRefreshToken @TokenHash NVARCHAR(128) AS BEGIN SET NOCOUNT ON; UPDATE dbo.RefreshTokens SET RevokedUtc=SYSUTCDATETIME() WHERE TokenHash=@TokenHash AND RevokedUtc IS NULL; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetGuestBookingByReference @BookingReference NVARCHAR(40),@MobileNumber NVARCHAR(20)=NULL AS BEGIN SET NOCOUNT ON; SELECT TOP 1 BookingId,BookingReference,MobileNumber,Status,BookingDateUtc FROM dbo.Bookings WHERE BookingReference=@BookingReference AND BookingSource='Guest' AND (@MobileNumber IS NULL OR MobileNumber=@MobileNumber); END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetUserByMobileNumber @MobileNumber NVARCHAR(20) AS BEGIN SET NOCOUNT ON; SELECT UserId,MobileNumber,Name,Email FROM dbo.Users WHERE MobileNumber=@MobileNumber AND IsActive=1; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking @UserId BIGINT=NULL,@BookingReference NVARCHAR(40),@BookingSource NVARCHAR(20),@GuestName NVARCHAR(200)=NULL,@MobileNumber NVARCHAR(20),@Email NVARCHAR(320)=NULL AS BEGIN SET NOCOUNT ON; INSERT dbo.Bookings(UserId,BookingReference,BookingSource,GuestName,MobileNumber,Email) VALUES(@UserId,@BookingReference,@BookingSource,@GuestName,@MobileNumber,@Email); SELECT CAST(SCOPE_IDENTITY() AS BIGINT) BookingId; END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateBookingAccessToken @BookingId BIGINT,@TokenHash NVARCHAR(128),@ExpiresUtc DATETIME2(3) AS BEGIN SET NOCOUNT ON; INSERT dbo.BookingAccessTokens(BookingId,TokenHash,ExpiresUtc) VALUES(@BookingId,@TokenHash,@ExpiresUtc); END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ValidateBookingAccessToken @BookingReference NVARCHAR(40),@TokenHash NVARCHAR(128) AS BEGIN SET NOCOUNT ON; SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.BookingAccessTokens a JOIN dbo.Bookings b ON b.BookingId=a.BookingId WHERE b.BookingReference=@BookingReference AND b.BookingSource='Guest' AND a.TokenHash=@TokenHash AND a.RevokedUtc IS NULL AND a.ExpiresUtc>SYSUTCDATETIME()) THEN 1 ELSE 0 END AS BIT); END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelGuestBookingWithOTP
	@BookingReference NVARCHAR(40),
	@MobileNumber NVARCHAR(20),
	@Purpose NVARCHAR(100),
	@OtpHash NVARCHAR(128)
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;
	DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
	DECLARE @otpId BIGINT, @attempts INT, @maxAttempts INT, @otpHashFromDb NVARCHAR(128), @expiresUtc DATETIME2(3), @isUsed BIT;
	DECLARE @bookingId BIGINT, @bookingStatus NVARCHAR(30);

	BEGIN TRAN;
	SELECT TOP (1)
		@otpId = OTPVerificationId,
		@attempts = Attempts,
		@maxAttempts = MaxAttempts,
		@otpHashFromDb = OTPHash,
		@expiresUtc = ExpiresUtc,
		@isUsed = IsUsed
	FROM dbo.OTPVerifications WITH (UPDLOCK, HOLDLOCK)
	WHERE MobileNumber = @MobileNumber AND Purpose = @Purpose
	ORDER BY CreatedDate DESC;

	IF @otpId IS NULL OR @isUsed = 1
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Success, 'OTP_NOT_FOUND' FailureCode, 0 CancelledCount;
		RETURN;
	END;
	IF @attempts >= @maxAttempts
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Success, 'OTP_MAX_ATTEMPTS' FailureCode, 0 CancelledCount;
		RETURN;
	END;
	IF @expiresUtc <= @now
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Success, 'OTP_EXPIRED' FailureCode, 0 CancelledCount;
		RETURN;
	END;
	IF @otpHashFromDb <> @OtpHash
	BEGIN
		UPDATE dbo.OTPVerifications SET Attempts = Attempts + 1 WHERE OTPVerificationId = @otpId;
		COMMIT;
		SELECT CAST(0 AS BIT) Success, 'OTP_INVALID' FailureCode, 0 CancelledCount;
		RETURN;
	END;

	SELECT TOP (1) @bookingId = BookingId, @bookingStatus = Status
	FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
	WHERE BookingReference = @BookingReference AND MobileNumber = @MobileNumber AND BookingSource = 'Guest';

	IF @bookingId IS NULL
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Success, 'BOOKING_NOT_FOUND' FailureCode, 0 CancelledCount;
		RETURN;
	END;
	IF @bookingStatus IN ('Cancelled', 'Completed')
	BEGIN
		ROLLBACK;
		SELECT CAST(0 AS BIT) Success, 'BOOKING_NOT_CANCELLABLE' FailureCode, 0 CancelledCount;
		RETURN;
	END;

	UPDATE dbo.OTPVerifications SET IsUsed = 1, UsedDate = @now WHERE OTPVerificationId = @otpId;
	UPDATE dbo.Bookings SET Status = 'Cancelled', ModifiedDate = @now WHERE BookingId = @bookingId;
	INSERT dbo.BookingCancellationAudit(BookingId, CancellationSource, MobileNumber, CancelledDateUtc)
	VALUES(@bookingId, 'GuestOTP', @MobileNumber, @now);
	COMMIT;

	SELECT CAST(1 AS BIT) Success, '' FailureCode, 1 CancelledCount;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelGuestBooking
	@BookingReference NVARCHAR(40),
	@MobileNumber NVARCHAR(20),
	@Purpose NVARCHAR(100),
	@OtpHash NVARCHAR(128)
AS
BEGIN
	SET NOCOUNT ON;
	EXEC dbo.usp_CancelGuestBookingWithOTP
		@BookingReference = @BookingReference,
		@MobileNumber = @MobileNumber,
		@Purpose = @Purpose,
		@OtpHash = @OtpHash;
END;
GO
