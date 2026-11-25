using System.Globalization;
using System.Net;
using System.Text;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Services;

namespace TaxHi.Infrastructure.Template;

/// <summary>Builds the subject and HTML body of the booking cancellation email. Pure and unit-testable.</summary>
public static class BookingCancellationEmailTemplate
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-IN");

    public static string BuildSubject(BookingConfirmationEmailDto d) => $"Your booking {d.BookingNo} has been cancelled";

    public static string BuildBody(BookingConfirmationEmailDto d, BookingCancellationEmailInfo info, BookingEmailBranding b)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f4f4f4;font-family:Arial,Helvetica,sans-serif;color:#333;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:16px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;\">");
        sb.Append($"<tr><td align=\"center\" style=\"background:#0b3d91;padding:18px 16px;font-size:24px;font-weight:bold;color:#ffffff;\">{E(b.BrandName)}</td></tr>");

        sb.Append("<tr><td style=\"padding:24px 24px 8px;font-size:14px;line-height:1.6;\">");
        sb.Append($"<p style=\"margin:0 0 12px;\">Hi {E(Or(d.CustomerName, "Customer"))},</p>");
        sb.Append($"<p style=\"margin:0 0 12px;\">Your booking <b>{E(d.BookingNo)}</b> has been cancelled. Here is a summary:</p></td></tr>");

        sb.Append("<tr><td style=\"padding:0 24px 16px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"8\" cellspacing=\"0\" style=\"border:1px solid #e0e0e0;font-size:14px;\">");
        Row(sb, "Booking ID", d.BookingNo);
        Row(sb, "Trip", $"{Or(d.PickupCity, "-")} to {Or(d.DropCity, "-")} ({Or(d.TripType, "-")})");
        if (!string.IsNullOrWhiteSpace(d.CarType)) Row(sb, "Car Type", d.CarType!);
        Row(sb, "Pickup time", d.PickupDateTime?.ToString("dddd, dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture) ?? "-");
        Row(sb, "Cancelled on", info.CancelledDate.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture));
        Row(sb, "Reason", info.Reason);
        if (!string.IsNullOrWhiteSpace(info.Remarks)) Row(sb, "Remarks", info.Remarks!);
        // Everything the accounts team needs to pay the refund manually.
        if (info.AmountPaid > 0) Row(sb, "Amount paid", $"Rs. {info.AmountPaid.ToString("N2", Culture)}");
        //if (!string.IsNullOrWhiteSpace(info.PaymentReference)) Row(sb, "Payment reference", info.PaymentReference!);
        Row(sb, "Customer", Or(d.CustomerName, "-"));
        if (!string.IsNullOrWhiteSpace(d.CustomerEmail)) Row(sb, "Customer email", d.CustomerEmail!);
        if (!string.IsNullOrWhiteSpace(d.CustomerPhone)) Row(sb, "Customer phone", d.CustomerPhone!);
        sb.Append("</table></td></tr>");

        sb.Append("<tr><td style=\"padding:0 24px 16px;font-size:13px;line-height:1.6;\">");
        sb.Append($"<p style=\"margin:0;\">We look forward to seeing you again soon. For any assistance, please contact us at <a href=\"mailto:{E(d.ContactPersonEmail)}\">{E(d.ContactPersonEmail)}</a>.</p>");
        sb.Append($"<p style=\"margin:16px 0 0;\">Thank You,<br><b>Team {E(b.BrandName)}</b></p></td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    public static string RefundText(BookingCancellationEmailInfo info) => info.RefundStatus switch
    {
        "Pending" => $"Rs. {info.RefundAmount.ToString("N2", Culture)} is approved. Our accounts team will process the refund manually and will contact you if they need anything.",
        _ => "No refund applies to this cancellation."
    };

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><td style=\"width:35%;background:#f7f7f7;border-bottom:1px solid #e0e0e0;font-weight:bold;\">{E(label)}</td>")
          .Append($"<td style=\"border-bottom:1px solid #e0e0e0;\">{E(value)}</td></tr>");

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
