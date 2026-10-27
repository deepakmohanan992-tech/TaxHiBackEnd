using TaxHi.Domain.Enums;

namespace TaxHi.Domain.Entities;

public sealed class Driver
{
    public Guid Id { get; }
    public string Name { get; }
    public string Phone { get; }
    public string LicenseNumber { get; }
    public IReadOnlyList<string> EligibleVehicleCategories { get; }
    public bool IsAvailable { get; private set; }

    public Driver(Guid id, string name, string phone, string licenseNumber, IEnumerable<string> eligibleVehicleCategories, bool isAvailable = true)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone is required.", nameof(phone));
        if (string.IsNullOrWhiteSpace(licenseNumber)) throw new ArgumentException("License number is required.", nameof(licenseNumber));
        Id = id;
        Name = name.Trim();
        Phone = phone.Trim();
        LicenseNumber = licenseNumber.Trim();
        EligibleVehicleCategories = eligibleVehicleCategories.Select(v => v.Trim().ToLowerInvariant()).Distinct().ToList().AsReadOnly();
        IsAvailable = isAvailable;
    }

    public bool CanDrive(string vehicleCategoryCode) =>
        EligibleVehicleCategories.Contains(vehicleCategoryCode.Trim().ToLowerInvariant());
}

public sealed class Payment
{
    public Guid Id { get; }
    public Guid BookingId { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public PaymentStatus Status { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Payment(Guid id, Guid bookingId, decimal amount, string currency)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        Id = id;
        BookingId = bookingId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void SetProviderOrder(string providerOrderId)
    {
        if (string.IsNullOrWhiteSpace(providerOrderId)) throw new ArgumentException("Provider order id is required.", nameof(providerOrderId));
        ProviderOrderId = providerOrderId.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorized() { Status = PaymentStatus.Authorized; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkPaid() { Status = PaymentStatus.Paid; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkFailed() { Status = PaymentStatus.Failed; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkCancelled() { Status = PaymentStatus.Cancelled; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkRefunded() { Status = PaymentStatus.Refunded; UpdatedAt = DateTimeOffset.UtcNow; }
}