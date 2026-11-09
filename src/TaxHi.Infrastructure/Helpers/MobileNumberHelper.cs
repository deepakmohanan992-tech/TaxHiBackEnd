namespace TaxHi.Infrastructure.Helpers;

public static class MobileNumberHelper
{
    public static string NormalizeMobile(string mobile) => mobile.Trim();

    public static bool IsValidMobile(string mobile) =>
        !string.IsNullOrWhiteSpace(mobile)
        && mobile.Trim().Length is >= 8 and <= 20
        && mobile.Trim().All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')');
}
