using TaxHi.Application.Exceptions;
using TaxHi.Application.Interfaces;
using TaxHi.Application.Features.Bookings.Dtos;
using TaxHi.Application.Features.FareQuotes;
using TaxHi.Application.Features.FareQuotes.Dtos;
using TaxHi.Domain.Entities;
using TaxHi.Domain.Enums;

namespace TaxHi.Application.Features.Bookings;

public sealed class BookingService
{
    private readonly IBookingStore _bookingStore;
    private readonly IFareQuoteStore _quoteStore;
    private readonly FareQuoteService _fareQuoteService;
    private readonly IVehicleCatalog _vehicleCatalog;
    private readonly IVehicleAvailabilityService _availability;
    private readonly IDriverService _driverService;
    private readonly INotificationService _notifications;
    private readonly IMessagePublisher _publisher;

    public BookingService(
        IBookingStore bookingStore,
        IFareQuoteStore quoteStore,
        FareQuoteService fareQuoteService,
        IVehicleCatalog vehicleCatalog,
        IVehicleAvailabilityService availability,
        IDriverService driverService,
        INotificationService notifications,
        IMessagePublisher publisher)
    {
        _bookingStore = bookingStore;
        _quoteStore = quoteStore;
        _fareQuoteService = fareQuoteService;
        _vehicleCatalog = vehicleCatalog;
        _availability = availability;
        _driverService = driverService;
        _notifications = notifications;
        _publisher = publisher;
    }

    public async Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var vehicle = _vehicleCatalog.GetByCode(request.VehicleCategory)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["vehicleCategory"] = new[] { "Unknown vehicle category." } });

        var avail = await _availability.GetAvailabilityAsync(request.Pickup, request.Drop, DateOnly.FromDateTime(request.TravelDateTime.Date), request.TripType, cancellationToken);
        var item = avail.FirstOrDefault(a => a.CategoryCode.Equals(vehicle.Code, StringComparison.OrdinalIgnoreCase));
        if (item is not null && !item.Available)
        {
            throw new ConflictException("Selected vehicle category is not available for the requested trip.");
        }

        decimal fareTotal;
        string currency = "INR";
        Guid? quoteId = request.QuoteId;

        if (quoteId.HasValue)
        {
            var quote = await _quoteStore.GetAsync(quoteId.Value, cancellationToken);
            if (quote is null) throw new NotFoundException("Quote not found.");
            if (quote.IsExpired(DateTimeOffset.UtcNow)) throw new ConflictException("Quote has expired. Please request a new quote.");
            if (!quote.Pickup.Equals(request.Pickup, StringComparison.OrdinalIgnoreCase) ||
                !quote.Drop.Equals(request.Drop, StringComparison.OrdinalIgnoreCase) ||
                quote.TripType != request.TripType ||
                !quote.VehicleCategoryCode.Equals(vehicle.Code, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Quote does not match booking details.");
            }
            fareTotal = quote.Breakdown.Total;
            currency = quote.Currency;
        }
        else
        {
            var qReq = new FareQuoteRequest(
                request.Pickup,
                request.Drop,
                request.TripType,
                request.TravelDateTime,
                request.ReturnDateTime,
                request.VehicleCategory,
                request.PassengerCount,
                null,
                null);
            var qResp = await _fareQuoteService.CreateAsync(qReq, cancellationToken);
            fareTotal = qResp.Breakdown.Total;
            currency = qResp.Currency;
            quoteId = qResp.QuoteId;
        }

        var booking = new Booking(
            Guid.NewGuid(),
            request.Pickup,
            request.Drop,
            request.TripType,
            vehicle.Code,
            request.TravelDateTime,
            request.ReturnDateTime,
            request.PassengerCount,
            request.ContactName,
            request.ContactPhone,
            request.ContactEmail,
            fareTotal,
            currency,
            quoteId);

        await _bookingStore.SaveAsync(booking, cancellationToken);

        await _notifications.NotifyBookingCreatedAsync(booking.Id, cancellationToken);
        await _publisher.PublishAsync("booking.created", new { booking.Id }, cancellationToken);

        return Map(booking);
    }

    public async Task<BookingResponse?> GetAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingStore.GetAsync(bookingId, cancellationToken);
        return booking is null ? null : Map(booking);
    }

    public async Task<IReadOnlyList<BookingResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var bookings = await _bookingStore.GetAllAsync(cancellationToken);
        return bookings.Select(Map).ToList().AsReadOnly();
    }

    public async Task<BookingResponse> CancelAsync(Guid bookingId, string? reason, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingStore.GetAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        booking.Cancel(reason);
        await _bookingStore.SaveAsync(booking, cancellationToken);

        await _notifications.NotifyBookingCancelledAsync(booking.Id, reason, cancellationToken);
        await _publisher.PublishAsync("booking.cancelled", new { booking.Id, reason }, cancellationToken);

        return Map(booking);
    }

    private static void Validate(CreateBookingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Pickup)) errors["pickup"] = new[] { "Pickup is required." };
        if (string.IsNullOrWhiteSpace(request.Drop)) errors["drop"] = new[] { "Drop is required." };
        if (!string.IsNullOrWhiteSpace(request.Pickup) && !string.IsNullOrWhiteSpace(request.Drop) &&
            request.Pickup.Trim().Equals(request.Drop.Trim(), StringComparison.OrdinalIgnoreCase))
            errors["drop"] = new[] { "Pickup and drop-off must be different." };
        if (request.PassengerCount <= 0) errors["passengerCount"] = new[] { "Passenger count must be greater than zero." };
        if (string.IsNullOrWhiteSpace(request.VehicleCategory)) errors["vehicleCategory"] = new[] { "Vehicle category is required." };
        if (string.IsNullOrWhiteSpace(request.ContactName)) errors["contactName"] = new[] { "Contact name is required." };
        if (string.IsNullOrWhiteSpace(request.ContactPhone)) errors["contactPhone"] = new[] { "Contact phone is required." };
        if (request.TripType == TripType.RoundTrip && request.ReturnDateTime is null)
            errors["returnDateTime"] = new[] { "Return date/time is required for round trips." };
        if (request.ReturnDateTime is not null && request.ReturnDateTime <= request.TravelDateTime)
            errors["returnDateTime"] = new[] { "Return date/time must be after travel date/time." };
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static BookingResponse Map(Booking b) => new(
        b.Id,
        b.Pickup,
        b.Drop,
        b.TripType,
        b.VehicleCategoryCode,
        b.TravelDateTime,
        b.ReturnDateTime,
        b.PassengerCount,
        b.ContactName,
        b.ContactPhone,
        b.ContactEmail,
        b.FareTotal,
        b.Currency,
        b.Status.ToString(),
        b.QuoteId,
        b.DriverAssignmentId,
        b.CreatedAt,
        b.CancelledAt,
        b.CancellationReason);
}