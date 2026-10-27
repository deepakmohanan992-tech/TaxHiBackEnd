using TaxHi.Domain.Enums;

namespace TaxHi.Domain.Entities;

public sealed class FareBreakdown
{
    public decimal BaseFare { get; }
    public decimal DistanceCharge { get; }
    public decimal MinimumDistanceCharge { get; }
    public decimal DriverAllowance { get; }
    public decimal WaitingCharges { get; }
    public decimal Tolls { get; }
    public decimal Parking { get; }
    public decimal Tax { get; }
    public decimal Discount { get; }
    public decimal OtherCharges { get; }
    public decimal Total { get; }

    public FareBreakdown(
        decimal baseFare,
        decimal distanceCharge,
        decimal minimumDistanceCharge = 0m,
        decimal driverAllowance = 0m,
        decimal waitingCharges = 0m,
        decimal tolls = 0m,
        decimal parking = 0m,
        decimal tax = 0m,
        decimal discount = 0m,
        decimal otherCharges = 0m)
    {
        BaseFare = baseFare;
        DistanceCharge = distanceCharge;
        MinimumDistanceCharge = minimumDistanceCharge;
        DriverAllowance = driverAllowance;
        WaitingCharges = waitingCharges;
        Tolls = tolls;
        Parking = parking;
        Tax = tax;
        Discount = discount;
        OtherCharges = otherCharges;
        Total = baseFare + distanceCharge + minimumDistanceCharge + driverAllowance + waitingCharges + tolls + parking + tax - discount + otherCharges;
        if (Total < 0) Total = 0;
    }
}

public sealed class FareQuote
{
    public Guid Id { get; }
    public string Pickup { get; }
    public string Drop { get; }
    public TripType TripType { get; }
    public string VehicleCategoryCode { get; }
    public DateTimeOffset TravelDateTime { get; }
    public DateTimeOffset? ReturnDateTime { get; }
    public int PassengerCount { get; }
    public double? DistanceKm { get; }
    public int? DurationMinutes { get; }
    public FareBreakdown Breakdown { get; }
    public string Currency { get; }
    public bool IsDevelopmentEstimate { get; }
    public string? Disclaimer { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public FareQuote(
        Guid id,
        string pickup,
        string drop,
        TripType tripType,
        string vehicleCategoryCode,
        DateTimeOffset travelDateTime,
        DateTimeOffset? returnDateTime,
        int passengerCount,
        FareBreakdown breakdown,
        string currency,
        bool isDevelopmentEstimate,
        DateTimeOffset expiresAt,
        double? distanceKm = null,
        int? durationMinutes = null,
        string? disclaimer = null)
    {
        Id = id;
        Pickup = pickup.Trim();
        Drop = drop.Trim();
        TripType = tripType;
        VehicleCategoryCode = vehicleCategoryCode.Trim().ToLowerInvariant();
        TravelDateTime = travelDateTime;
        ReturnDateTime = returnDateTime;
        PassengerCount = passengerCount;
        Breakdown = breakdown;
        Currency = currency.Trim().ToUpperInvariant();
        IsDevelopmentEstimate = isDevelopmentEstimate;
        ExpiresAt = expiresAt;
        DistanceKm = distanceKm;
        DurationMinutes = durationMinutes;
        Disclaimer = disclaimer;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}