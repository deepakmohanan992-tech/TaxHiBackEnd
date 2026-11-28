using System.Globalization;
using System.Net;
using System.Text;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Infrastructure.Template;

/// <summary>Subject and HTML body of the vendor-facing "new booking" email. Pure and unit-testable.</summary>
public static class BookingVendorConfirmationEmailTemplate
{
    public static string BuildSubject(BookingConfirmationEmailDto d) =>
        $"New booking confirmed: {Or(d.TripType, "")} trip from {Or(d.PickupCity, "")} on {ShortDate(d.PickupDateTime)} (ID: {d.BookingNo})"
            .Replace("  ", " ");

    public static string BuildBody(BookingConfirmationEmailDto d, BookingEmailBranding b) =>
        BuildLayout(d, b, new BodyCopy(
            "NEW BOOKING",
            $"{E(Or(d.CustomerName, "A customer"))} has booked a trip on {E(FullDate(d.PickupDateTime))}.",
            $"Hello {E(Or(d.VendorName, "Partner"))},",
            $"A new booking <b>{E(d.BookingNo)}</b> has been confirmed for your fleet. The booking summary is below; the attached statement has the fare breakdown and your payout for this trip.",
            "Guest phone",
            Or(d.CustomerPhone, "-"),
            $"Thank you for partnering with <b>{E(b.BrandName)}</b>."));

    /// <summary>The words that differ between the vendor and the customer email; <c>Headline</c>, <c>Greeting</c>, <c>Intro</c> and <c>ThankYou</c> are HTML (already encoded).</summary>
    internal sealed record BodyCopy(string Eyebrow, string Headline, string Greeting, string Intro, string PhoneLabel, string PhoneValue, string ThankYou);

    /// <summary>The one message layout shared by the vendor and customer confirmation emails.</summary>
    internal static string BuildLayout(BookingConfirmationEmailDto d, BookingEmailBranding b, BodyCopy copy)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#FFF7EA;font-family:Arial,Helvetica,sans-serif;color:#252525;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:16px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#FFFEFC;\">");

        sb.Append("<tr><td align=\"center\" style=\"padding:28px 24px 8px;\">");
        if (Url(b.LogoUrl) is { } logo)
            sb.Append($"<img src=\"{E(logo)}\" alt=\"{E(b.BrandName)}\" height=\"40\" style=\"display:block;border:0;margin:0 auto 12px;\">");
        else
            sb.Append($"<div style=\"font-size:24px;color:#3F6B5C;margin-bottom:12px;\">{E(b.BrandName)}</div>");
        sb.Append($"<div style=\"font-size:11px;letter-spacing:1.4px;font-weight:bold;color:#3F6B5C;\">{copy.Eyebrow}</div>");
        sb.Append($"<div style=\"font-size:18px;margin-top:8px;\">{copy.Headline}</div>");
        sb.Append("</td></tr>");

        sb.Append("<tr><td style=\"padding:8px 24px 0;font-size:14px;line-height:1.6;\">");
        sb.Append($"<p style=\"margin:0 0 12px;\">{copy.Greeting}</p>");
        sb.Append($"<p style=\"margin:0 0 12px;\">{copy.Intro}</p>");
        sb.Append("</td></tr>");

        sb.Append("<tr><td style=\"padding:0 24px 16px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"8\" cellspacing=\"0\" style=\"border:1px solid #EDE6DA;font-size:14px;\">");
        Row(sb, "Booking reference", d.BookingNo);
        Row(sb, "Guest", Or(d.CustomerName, "-"));
        Row(sb, copy.PhoneLabel, copy.PhoneValue);
        Row(sb, "Trip", string.Join(" → ", new[] { Or(d.PickupCity, "-"), Or(d.DropCity, "-") }) + (string.IsNullOrWhiteSpace(d.TripType) ? "" : $" ({d.TripType!.Trim()})"));
        Row(sb, "Pickup time", d.PickupDateTime?.ToString("dd-MM-yyyy hh:mm tt", CultureInfo.InvariantCulture) ?? "-");
        Row(sb, "Pickup location", Or(d.PickupLocation, "-"));
        Row(sb, "Drop location", Or(d.DropLocation, "-"));
        Row(sb, "Car type", Or(d.CarType, "-"));
        sb.Append("</table></td></tr>");

        sb.Append("<tr><td style=\"padding:0 24px 24px;font-size:13px;line-height:1.6;color:#555;\">");
        sb.Append($"<p style=\"margin:0 0 8px;\">For amendments, pickup updates or payment queries, please contact {E(b.BrandName)} Reservations");
        if (!string.IsNullOrWhiteSpace(b.HelplineEmail)) sb.Append($" at <a href=\"mailto:{E(b.HelplineEmail)}\">{E(b.HelplineEmail)}</a>");
        sb.Append(".</p>");
        sb.Append($"<p style=\"margin:12px 0 0;\">{copy.ThankYou}</p>");
        sb.Append("</td></tr></table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><td style=\"width:38%;background:#F3F8F6;border-bottom:1px solid #EDE6DA;font-weight:bold;\">{E(label)}</td>")
          .Append($"<td style=\"border-bottom:1px solid #EDE6DA;\">{E(value)}</td></tr>");

    private static string ShortDate(DateTime? d) => d?.ToString("dd MMM", CultureInfo.InvariantCulture) ?? "";
    internal static string FullDate(DateTime? d) => d?.ToString("dddd, dd MMM yyyy", CultureInfo.InvariantCulture) ?? "the scheduled date";
    internal static string Or(string? v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
    internal static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

    private static string? Url(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
}
