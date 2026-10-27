using TaxHi.Domain.Enums;

namespace TaxHi.Domain.Entities;

public sealed class Booking
{
    public Guid Id { get; }
    public string Pickup { get; }
    public string Drop { get; }
    public TripType TripType { get; }
    public string VehicleCategoryCode { get; }
    public DateTimeOffset TravelDateTime { get; }
    public DateTimeOffset? ReturnDateTime { get; }
    public int PassengerCount { get; }
    public string ContactName { get; }
    public string ContactPhone { get; }
    public string? ContactEmail { get; }
    public decimal FareTotal { get; private set; }
    public string Currency { get; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? QuoteId { get; }
    public string? DriverAssignmentId { get; private set; }

    public Booking(
        Guid id,
        string pickup,
        string drop,
        TripType tripType,
        string vehicleCategoryCode,
        DateTimeOffset travelDateTime,
        DateTimeOffset? returnDateTime,
        int passengerCount,
        string contactName,
        string contactPhone,
        string? contactEmail,
        decimal fareTotal,
        string currency,
        Guid? quoteId = null)
    {
        if (string.IsNullOrWhiteSpace(pickup)) throw new ArgumentException("Pickup is required.", nameof(pickup));
        if (string.IsNullOrWhiteSpace(drop)) throw new ArgumentException("Drop is required.", nameof(drop));
        if (pickup.Trim().Equals(drop.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pickup and drop-off must be different.", nameof(drop));
        if (string.IsNullOrWhiteSpace(vehicleCategoryCode)) throw new ArgumentException("Vehicle category is required.", nameof(vehicleCategoryCode));
        if (passengerCount <= 0) throw new ArgumentOutOfRangeException(nameof(passengerCount));
        if (string.IsNullOrWhiteSpace(contactName)) throw new ArgumentException("Contact name is required.", nameof(contactName));
        if (string.IsNullOrWhiteSpace(contactPhone)) throw new ArgumentException("Contact phone is required.", nameof(contactPhone));
        if (fareTotal < 0) throw new ArgumentOutOfRangeException(nameof(fareTotal));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        if (tripType == TripType.RoundTrip && returnDateTime is null)
            throw new ArgumentException("Return date/time is required for round trips.", nameof(returnDateTime));
        if (returnDateTime is not null && returnDateTime <= travelDateTime)
            throw new ArgumentException("Return date/time must be after travel date/time.", nameof(returnDateTime));

        Id = id;
        Pickup = pickup.Trim();
        Drop = drop.Trim();
        TripType = tripType;
        VehicleCategoryCode = vehicleCategoryCode.Trim().ToLowerInvariant();
        TravelDateTime = travelDateTime;
        ReturnDateTime = returnDateTime;
        PassengerCount = passengerCount;
        ContactName = contactName.Trim();
        ContactPhone = contactPhone.Trim();
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        FareTotal = fareTotal;
        Currency = currency.Trim().ToUpperInvariant();
        QuoteId = quoteId;
        Status = BookingStatus.Confirmed;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status == BookingStatus.Cancelled) throw new InvalidOperationException("Booking is already cancelled.");
        if (Status == BookingStatus.Completed) throw new InvalidOperationException("Completed booking cannot be cancelled.");
        if (Status == BookingStatus.InProgress) throw new InvalidOperationException("In-progress booking cannot be cancelled.");
        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void AssignDriver(string assignmentId)
    {
        if (Status != BookingStatus.Confirmed) throw new InvalidOperationException("Driver can only be assigned to a confirmed booking.");
        if (string.IsNullOrWhiteSpace(assignmentId)) throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        DriverAssignmentId = assignmentId.Trim();
        Status = BookingStatus.DriverAssigned;
    }

    public void StartTrip()
    {
        if (Status != BookingStatus.DriverAssigned) throw new InvalidOperationException("Trip can only start after driver assignment.");
        Status = BookingStatus.InProgress;
    }

    public void Complete()
    {
        if (Status != BookingStatus.InProgress) throw new InvalidOperationException("Only in-progress booking can be completed.");
        Status = BookingStatus.Completed;
    }

    public static bool IsValidTransition(BookingStatus from, BookingStatus to) =>
        (from, to) switch
        {
            (BookingStatus.Draft, BookingStatus.Confirmed) => true,
            (BookingStatus.Confirmed, BookingStatus.DriverAssigned) => true,
            (BookingStatus.Confirmed, BookingStatus.Cancelled) => true,
            (BookingStatus.DriverAssigned, BookingStatus.InProgress) => true,
            (BookingStatus.DriverAssigned, BookingStatus.Cancelled) => true,
            (BookingStatus.InProgress, BookingStatus.Completed) => true,
            _ => false
        };
}