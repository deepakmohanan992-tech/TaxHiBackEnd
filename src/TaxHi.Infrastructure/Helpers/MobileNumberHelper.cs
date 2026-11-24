namespace TaxHi.Infrastructure.Helpers;

public static class MobileNumberHelper
{
    public static string NormalizeMobile(string mobile) => mobile.Trim();

    /// <summary>Keeps only the last four digits, e.g. <c>+91 ******3210</c> (a 10-digit number gets the +91 prefix).</summary>
    public static string MaskMobile(string mobile)
    {
        var digits = new string((mobile ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return digits;
        var masked = new string('*', digits.Length - 4) + digits[^4..];
        return digits.Length == 10 ? "+91 " + masked : masked;
    }

    public static bool IsValidMobile(string mobile) =>
        !string.IsNullOrWhiteSpace(mobile)
        && mobile.Trim().Length is >= 8 and <= 20
        && mobile.Trim().All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')');
}
