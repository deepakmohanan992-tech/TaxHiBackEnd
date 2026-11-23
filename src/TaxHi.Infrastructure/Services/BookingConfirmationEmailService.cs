using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxHi.Application.Models.Booking;
using TaxHi.Application.Repositories;
using TaxHi.Application.Services;
using TaxHi.Infrastructure.Template;

namespace TaxHi.Infrastructure.Services;

/// <summary>
/// Sends the booking confirmation through the vendor's Gmail account. Everything it needs (vendor SMTP
/// credentials, user and vendor ids) is read from the database by booking id, so it works outside an HTTP
/// session. It never throws: failures go to the vendor audit log and the application log.
/// </summary>
public sealed class BookingConfirmationEmailService(
    IBookingEmailRepository repository,
    IBookingInvoiceService invoiceService,
    IConfiguration configuration,
    ILogger<BookingConfirmationEmailService> logger) : IBookingConfirmationEmailService
{
    private const string SentAction = "BookingConfirmationEmailSent";
    private const string FailedAction = "BookingConfirmationEmailFailed";

    public async Task SendBookingConfirmationAsync(long bookingId, long userId, CancellationToken cancellationToken = default)
    {
        BookingConfirmationEmailDto? details = null;
        try
        {
            details = await repository.GetConfirmationEmailDetailsAsync(bookingId, userId, cancellationToken);
            if (details is null)
            {
                logger.LogWarning("Confirmation email skipped: booking {BookingId} not found for user {UserId}.", bookingId, userId);
                return;
            }

            if (string.IsNullOrWhiteSpace(details.CustomerEmail))
                throw new InvalidOperationException("The booking has no customer email address.");
            if (string.IsNullOrWhiteSpace(details.GmailUserName) || string.IsNullOrWhiteSpace(details.GmailAppPassword))
                throw new InvalidOperationException($"Vendor {details.VendorId} has no Gmail credentials configured.");

            var branding = LoadBranding(details);
            using var message = BuildMessage(details, branding);
            await SendAsync(message, details, cancellationToken);

            await TryAuditAsync(details, SentAction, $"Booking {details.BookingNo}: confirmation email sent to {details.CustomerEmail}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking confirmation email failed for booking {BookingId}.", bookingId);
            // The vendor id is only known once the booking has been read; 0 marks "unknown".
            await TryAuditAsync(details?.VendorId ?? 0, details?.UserId ?? userId, FailedAction,
                $"Booking {details?.BookingNo ?? bookingId.ToString()}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private MailMessage BuildMessage(BookingConfirmationEmailDto d, BookingEmailBranding branding)
    {
        var message = new MailMessage
        {
            From = new MailAddress(d.GmailUserName!.Trim(), string.IsNullOrWhiteSpace(d.VendorName) ? branding.BrandName : d.VendorName.Trim()),
            Subject = BookingConfirmationEmailTemplate.BuildSubject(d),
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
            Body = BookingConfirmationEmailTemplate.BuildBody(d, branding)
        };

        message.To.Add(new MailAddress(d.CustomerEmail!.Trim()));

        // Same CC rule as the legacy SendMail: reservation staff and contact person, when present.
        foreach (var cc in new[] { d.ReservationStaffEmail, d.ContactPersonEmail }
                     .SelectMany(SplitAddresses)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(cc, d.CustomerEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                message.CC.Add(new MailAddress(cc));
        }

        // The invoice is generated before this runs; a missing PDF degrades to an email without attachment.
        var pdfPath = invoiceService.ResolveExistingInvoicePath(d.InvoiceFilePath);
        if (pdfPath is null)
            logger.LogWarning("Invoice PDF not found for booking {BookingId}; sending the confirmation without it.", d.BookingId);
        else
            message.Attachments.Add(new Attachment(pdfPath, MediaTypeNames.Application.Pdf) { Name = $"{SafeFileName(d.BookingNo)}.pdf" });

        return message;
    }

    private async Task SendAsync(MailMessage message, BookingConfirmationEmailDto d, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(configuration["BookingEmail:SmtpHost"] is { Length: > 0 } host ? host : "smtp.gmail.com",
            int.TryParse(configuration["BookingEmail:SmtpPort"], out var port) ? port : 587)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(d.GmailUserName!.Trim(), d.GmailAppPassword!.Trim()),
            Timeout = 30_000
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    private BookingEmailBranding LoadBranding(BookingConfirmationEmailDto d)
    {
        string? Get(string key) => configuration[$"BookingEmail:{key}"] is { Length: > 0 } v ? v : null;
        var brand = Get("BrandName")
            ?? (string.IsNullOrWhiteSpace(d.VendorName) ? null : d.VendorName.Trim())
            ?? configuration["Invoice:CompanyName"]
            ?? "TaxHi";

        return new BookingEmailBranding(
            brand,
            Get("LogoUrl"),
            Get("HeaderBannerUrl"),
            Get("FooterBannerUrl"),
            Get("TermsUrl") ?? (d.VendorWebsite is { Length: > 0 } w ? w : configuration["Invoice:Website"]) ?? "",
            Get("HelplinePhone") ?? configuration["Invoice:SupportPhone"] ?? "",
            Get("HelplineEmail") ?? configuration["Invoice:SupportEmail"] ?? "");
    }

    private Task TryAuditAsync(BookingConfirmationEmailDto d, string action, string text) =>
        TryAuditAsync(d.VendorId, d.UserId, action, text);

    /// <summary>Audit writing is best effort too: a logging problem must not surface either.</summary>
    private async Task TryAuditAsync(int vendorId, long userId, string action, string text)
    {
        try
        {
            // CancellationToken.None: the request may already be aborted, the audit row is still wanted.
            await repository.SaveAuditLogAsync(vendorId, userId, action, Truncate(text, 2000), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the booking email audit log ({Action}).", action);
        }
    }

    private static IEnumerable<string> SplitAddresses(string? value) =>
        (value ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => MailAddress.TryCreate(a, out _));

    private static string SafeFileName(string bookingNo)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(bookingNo.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "booking" : cleaned;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
