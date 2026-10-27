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
- No authentication/authorization implemented yet.
- No real external integrations (maps, payments, notifications, message broker, Redis).
- All availability/notifications/messages are simulated and clearly labelled.