using TaxHi.Domain.Enums;

namespace TaxHi.UnitTests;

public class BookingRulesTests
{
    [Fact]
    public void IsValidTransition_ConfirmedToCancelled_True()
    {
        Assert.True(TaxHi.Domain.Entities.Booking.IsValidTransition(TaxHi.Domain.Enums.BookingStatus.Confirmed, TaxHi.Domain.Enums.BookingStatus.Cancelled));
    }

    [Fact]
    public void IsValidTransition_CompletedToCancelled_False()
    {
        Assert.False(TaxHi.Domain.Entities.Booking.IsValidTransition(TaxHi.Domain.Enums.BookingStatus.Completed, TaxHi.Domain.Enums.BookingStatus.Cancelled));
    }

    [Fact]
    public void Cancel_WhenCompleted_Throws()
    {
        // Build a booking then force complete via reflection? Instead test Cancel on fresh confirmed then complete.
        var booking = new TaxHi.Domain.Entities.Booking(
            Guid.NewGuid(), "A", "B", TaxHi.Domain.Enums.TripType.OneWay, "sedan",
            DateTimeOffset.UtcNow.AddDays(1), null, 2, "Name", "999", null, 100m, "INR");
        booking.AssignDriver("drv-1");
        booking.StartTrip();
        booking.Complete();
        Assert.Throws<InvalidOperationException>(() => booking.Cancel("no"));
    }

    [Fact]
    public void Constructor_PickupEqualsDrop_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TaxHi.Domain.Entities.Booking(
            Guid.NewGuid(), "A", "A", TaxHi.Domain.Enums.TripType.OneWay, "sedan",
            DateTimeOffset.UtcNow.AddDays(1), null, 2, "Name", "999", null, 100m, "INR"));
    }
}