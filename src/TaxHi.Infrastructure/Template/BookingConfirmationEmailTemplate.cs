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

    public static string BuildBody(BookingConfirmationEmailDto d, BookingEmailBranding b)
    {
        var tripType = Or(d.TripType, "");
        var pickupCity = Or(d.PickupCity, "");
        var dropCity = Or(d.DropCity, "");
        var tripLine = d.DistanceKm is { } km
            ? $"{pickupCity} → {dropCity} ({km.ToString("0.##", Culture)} km {tripType})"
            : $"{pickupCity} → {dropCity} ({tripType})";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f4f4f4;font-family:Arial,Helvetica,sans-serif;color:#333;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:16px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;\">");

        // Header banner: brand logo + tagline.
        sb.Append("<tr><td align=\"center\" style=\"background:#0b3d91;padding:18px 16px;\">");
        if (Url(b.HeaderBannerUrl) is { } header)
            sb.Append($"<img src=\"{E(header)}\" alt=\"{E(b.BrandName)}\" width=\"568\" style=\"max-width:100%;display:block;border:0;\">");
        else
        {
            if (Url(b.LogoUrl) is { } logo)
                sb.Append($"<img src=\"{E(logo)}\" alt=\"{E(b.BrandName)}\" height=\"48\" style=\"display:block;border:0;margin:0 auto 8px;\">");
            else
                sb.Append($"<div style=\"font-size:26px;font-weight:bold;color:#ffffff;\">{E(b.BrandName)}</div>");
            sb.Append("<div style=\"font-size:14px;letter-spacing:2px;color:#ffd54f;font-weight:bold;\">YOUR SAFETY IS OUR PRIORITY!</div>");
        }
        sb.Append("</td></tr>");

        sb.Append("<tr><td style=\"padding:24px 24px 8px;font-size:14px;line-height:1.6;\">");
        sb.Append($"<p style=\"margin:0 0 12px;\">Hi {E(Or(d.CustomerName, "Customer"))},</p>");
        sb.Append($"<p style=\"margin:0 0 12px;\">Your {E(tripType)} booking ID <b>{E(d.BookingNo)}</b>, on {E(FullDate(d.PickupDateTime))} from {E(pickupCity)} to {E(Or(d.DropLocation, dropCity))} is confirmed! ");
        sb.Append("You will receive your driver details within 1 hour of your pick up time. ");
        sb.Append("We seek your cooperation to avoid enquiring about the driver details before the specified time. ");
        sb.Append("Kindly check the attachment for the complete billing details. Below is a summary of your booking:</p>");
        sb.Append("</td></tr>");

        // Booking summary.
        sb.Append("<tr><td style=\"padding:0 24px 16px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"8\" cellspacing=\"0\" style=\"border:1px solid #e0e0e0;font-size:14px;\">");
        Row(sb, "Pickup time", PickupTime(d.PickupDateTime));
        Row(sb, "Pickup Location", Or(d.PickupLocation, "-"));
        Row(sb, "Drop Location", Or(d.DropLocation, "-"));
        Row(sb, "Trip Details", tripLine);
        Row(sb, "Car Type", Or(d.CarType, "-"));
        Row(sb, "Phone No", MaskPhone(d.CustomerPhone));
        sb.Append("</table></td></tr>");

        // Terms & conditions.
        sb.Append("<tr><td style=\"padding:0 24px 16px;font-size:13px;line-height:1.6;\">");
        sb.Append("<p style=\"margin:0 0 6px;font-weight:bold;\">Terms &amp; Conditions</p><ul style=\"margin:0;padding-left:18px;\">");
        sb.Append(d.IncludedKilometres is { } inc && d.ExtraKmRate is { } rate
            ? $"<li>Your booking includes {E(inc.ToString("0.##", Culture))} km. Kilometres travelled beyond this limit are charged at ₹{E(rate.ToString("0.##", Culture))} per km.</li>"
            : "<li>Kilometres travelled beyond the booked limit are charged as per the extra kilometre rate.</li>");
        sb.Append("<li>The booking covers a single pickup and a single drop. Additional stops or locations are not included.</li>");
        sb.Append("<li>The air conditioner may be switched off during hill climbs.</li>");
        sb.Append("</ul>");
        if (Url(b.TermsUrl) is { } terms)
            sb.Append($"<p style=\"margin:8px 0 0;\">For detailed terms and conditions, <a href=\"{E(terms)}\">click here</a>.</p>");
        sb.Append($"<p style=\"margin:12px 0 0;\">For any assistance, please contact us at <a href=\"mailto:{E(d.ContactPersonEmail)}\">{E(d.ContactPersonEmail)}</a>.</p>");
        sb.Append($"<p style=\"margin:16px 0 0;\">Thank You,<br><b>Team {E(b.BrandName)}</b></p>");
        sb.Append("</td></tr>");

        // Footer banner.
        sb.Append("<tr><td align=\"center\" style=\"background:#0b3d91;padding:12px 16px;color:#ffffff;font-size:12px;\">");
        if (Url(b.FooterBannerUrl) is { } footer)
            sb.Append($"<img src=\"{E(footer)}\" alt=\"{E(b.BrandName)}\" width=\"568\" style=\"max-width:100%;display:block;border:0;\">");
        else
            sb.Append($"{E(b.BrandName)} &middot; YOUR SAFETY IS OUR PRIORITY!");
        sb.Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><td style=\"width:35%;background:#f7f7f7;border-bottom:1px solid #e0e0e0;font-weight:bold;\">{E(label)}</td>")
          .Append($"<td style=\"border-bottom:1px solid #e0e0e0;\">{E(value)}</td></tr>");

    private static string ShortDate(DateTime? d) => d?.ToString("dd MMM", CultureInfo.InvariantCulture) ?? "";
    private static string FullDate(DateTime? d) => d?.ToString("dddd, dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
    private static string PickupTime(DateTime? d) => d?.ToString("hh:mm tt", CultureInfo.InvariantCulture) ?? "-";

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Only absolute http(s) URLs are emitted into href/src.</summary>
    private static string? Url(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
}
