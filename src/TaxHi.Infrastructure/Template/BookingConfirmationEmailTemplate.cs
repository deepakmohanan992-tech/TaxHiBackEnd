using System.Globalization;
using System.Net;
using System.Text;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Infrastructure.Template;

/// <summary>Branding and links that are configuration, not booking data.</summary>
public sealed record BookingEmailBranding(
    string BrandName,
    string? LogoUrl,
    string? HeaderBannerUrl,
    string? FooterBannerUrl,
    string TermsUrl,
    string HelplinePhone,
    string HelplineEmail);

/// <summary>Builds the subject and HTML body of the booking confirmation email. Pure and unit-testable.</summary>
public static class BookingConfirmationEmailTemplate
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-IN");

    public static string BuildSubject(BookingConfirmationEmailDto d) =>
        $"Your confirmed {Or(d.TripType, "")} trip booking from {Or(d.PickupCity, "")} on {ShortDate(d.PickupDateTime)} (ID: {d.BookingNo})"
            .Replace("  ", " ");

    /// <summary>Keeps only the last four digits visible, e.g. <c>********1154</c>.</summary>
    public static string MaskPhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return "-";
        if (digits.Length <= 4) return digits;
        return new string('*', digits.Length - 4) + digits[^4..];
    }

    /// <summary>Same message layout as the vendor email (<see cref="BookingVendorConfirmationEmailTemplate"/>); only the wording differs.</summary>
    public static string BuildBody(BookingConfirmationEmailDto d, BookingEmailBranding b) =>
        BookingVendorConfirmationEmailTemplate.BuildLayout(d, b, new BookingVendorConfirmationEmailTemplate.BodyCopy(
            "BOOKING CONFIRMED",
            $"Your trip on {E(BookingVendorConfirmationEmailTemplate.FullDate(d.PickupDateTime))} is confirmed!",
            $"Hello {E(Or(d.CustomerName, "Customer"))},",
            $"Your booking <b>{E(d.BookingNo)}</b> with {E(Or(d.VendorName, b.BrandName))} is confirmed. The booking summary is below; the attached invoice has the complete billing details. You will receive your driver details within 1 hour of your pick up time.",
            "Phone",
            MaskPhone(d.CustomerPhone),
            $"Thank you for choosing <b>{E(b.BrandName)}</b>."));

    private static string ShortDate(DateTime? d) => d?.ToString("dd MMM", CultureInfo.InvariantCulture) ?? "";

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
