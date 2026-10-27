namespace TaxHi.Domain.Enums;

public enum TripType
{
    OneWay = 1,
    RoundTrip = 2,
    AirportTransfer = 3
}

public enum BookingStatus
{
    Draft = 1,
    Confirmed = 2,
    DriverAssigned = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6
}

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Paid = 3,
    Failed = 4,
    Refunded = 5,
    Cancelled = 6
}

public enum LocationType
{
    City = 1,
    Airport = 2,
    Place = 3
}