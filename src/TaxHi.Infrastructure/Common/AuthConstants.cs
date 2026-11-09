namespace TaxHi.Infrastructure.Common;

public static class AuthConstants
{
    public static string GuestPurpose(string bookingReference) =>
        $"GuestCancellation:{bookingReference.Trim().ToUpperInvariant()}";
}
