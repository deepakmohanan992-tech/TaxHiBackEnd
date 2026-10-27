namespace TaxHi.Domain.Entities;

public sealed class VehicleCategory
{
    public string Code { get; }
    public string DisplayName { get; }
    public int PassengerCapacity { get; }
    public int LuggageCapacity { get; }
    public IReadOnlyList<string> Features { get; }

    public VehicleCategory(string code, string displayName, int passengerCapacity, int luggageCapacity, IEnumerable<string>? features = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        if (passengerCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(passengerCapacity));
        if (luggageCapacity < 0) throw new ArgumentOutOfRangeException(nameof(luggageCapacity));
        Code = code.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        PassengerCapacity = passengerCapacity;
        LuggageCapacity = luggageCapacity;
        Features = (features ?? Array.Empty<string>()).Select(f => f.Trim()).Where(f => f.Length > 0).ToList().AsReadOnly();
    }
}