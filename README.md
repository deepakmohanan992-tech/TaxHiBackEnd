# TaxHi Outstation Cab Booking Backend

ASP.NET Core Web API (.NET 10) implementing Savaari-style outstation cab booking workflows.

## Run

```powershell
cd d:\TaxHi\src\TaxHi.Api
dotnet run
```

Swagger UI: http://localhost:5120/swagger

Health: http://localhost:5120/health

## Test

```powershell
cd d:\TaxHi
dotnet test
```

## Notes

- In-memory development data only. Bookings, quotes, payments are not durable and are lost on restart.
- Authentication endpoints are implemented under `api/Authentication`; execute `database/Authentication.sql` against the target SQL Server database before using them.
- Configure `Authentication:JwtSigningKey` with a secret held outside source control. The checked-in value is only a development placeholder.
- `NullOtpNotifier` intentionally performs no delivery because this repository has no SMS provider. Register a production `IOtpNotifier` implementation for real OTP delivery; OTP values are never included in API responses or logs.
- No real external integrations (maps, payments, notifications, message broker, Redis).
- All availability/notifications/messages are simulated and clearly labelled.

### Authentication examples

`POST /api/Authentication/SendOTP`

```json
{ "mobileNumber": "+919999999999", "purpose": "Login" }
```

`POST /api/Authentication/VerifyOTP`

```json
{ "mobileNumber": "+919999999999", "otp": "123456", "purpose": "Login" }
```

The successful response contains the short-lived JWT access token, a one-time-visible refresh token, expiry, and registered user information. Store the access token in the frontend’s protected token mechanism and send it as `Authorization: Bearer <token>`; do not put either token in a URL.

Guest cancellation requires the booking reference and mobile number for both OTP requests and verification. The server verifies the booking, binds the OTP purpose to that booking reference, enforces expiry/attempt/cooldown rules, and writes cancellation audit data through `usp_CancelGuestBooking`.

The existing vendor redirect continues to use the encoded `VendorId` flow. `VendorId` identifies the vendor and routes the booking flow; it is not proof of user authentication. Do not use it as a substitute for OTP verification or JWT authorization.
