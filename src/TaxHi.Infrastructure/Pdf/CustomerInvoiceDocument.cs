using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models.Booking;
using static TaxHi.Infrastructure.Pdf.InvoicePdfStyle;

namespace TaxHi.Infrastructure.Pdf;

/// <summary>Customer-facing booking invoice ("Booking confirmed"): trip, fare, payment and notes.</summary>
public sealed class CustomerInvoiceDocument(
    BookingInvoiceDto data, IConfiguration configuration, byte[]? logo = null, byte[]? vehicleImage = null) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Booking Invoice {data.BookingNo}",
        Author = data.VendorName,
        Subject = "Customer booking invoice"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container) => Frame(container, col =>
    {
        var brand = Brand(configuration);

        Wordmark(col, brand, logo);
        Eyebrow(col, "Booking confirmed", top: 22);
        Heading(col, string.IsNullOrWhiteSpace(data.VendorName) ? brand : data.VendorName);
        Hero(col, vehicleImage, data.VehicleName);
        DateBand(col, "Pickup", data.PickupDateTime, "Drop", DropMoment(data), Route(data));

        col.Item().PaddingTop(20).Text(t =>
        {
            t.Span("Dear ");
            t.Span(string.IsNullOrWhiteSpace(data.CustomerName) ? "Customer" : data.CustomerName).Bold();
            t.Span(",");
        });
        Paragraph(col, $"Greetings from {brand}!");
        Paragraph(col, $"Thank you for choosing {brand} for your upcoming trip. We are delighted to confirm your booking with {data.VendorName}. Please review the details below.");

        Section(col, "Your trip");
        Detail(col, "Booking reference", data.BookingNo, bold: true);
        Detail(col, "Guest", data.CustomerName, bold: true);
        Detail(col, "Vendor", data.VendorName, bold: true);
        Detail(col, "Trip", TripDetail(data));
        Detail(col, "Pickup location", data.PickupLocation);
        Detail(col, "Drop location", data.DropLocation);
        Detail(col, "Vehicle", data.VehicleName, bold: true);
        Detail(col, "Fuel type", data.FuelType, skipIfEmpty: true);
        Detail(col, "Registration no", data.RegistrationNo, skipIfEmpty: true);
        Detail(col, "Driver", data.DriverName, skipIfEmpty: true);
        Detail(col, "Driver contact", data.DriverMobile, skipIfEmpty: true);
        if (data.HasGST)
        {
            Detail(col, "GST number", data.GSTNumber);
            Detail(col, "GST company", data.GSTCompanyName, skipIfEmpty: true);
        }
        Detail(col, "Booked on", Date(data.CreatedDate));
        Detail(col, "Status", data.BookingStatus);

        Section(col, "Payment", top: 26);
        Amount(col, "Base fare", data.BaseFare, muted: true, size: 8.5f, rule: true);
        if (data.DriverAllowance > 0) Amount(col, "Driver allowance", data.DriverAllowance, muted: true, size: 8.5f, rule: true);
        if (data.AddOnsTotalAmount > 0) Amount(col, "Add-ons", data.AddOnsTotalAmount, muted: true, size: 8.5f, rule: true);
        if (data.DiscountAmount > 0)
            Amount(col, string.IsNullOrWhiteSpace(data.CouponCode) ? "Discount" : $"Discount ({data.CouponCode})", -data.DiscountAmount, muted: true, size: 8.5f, rule: true);
        Amount(col, "GST", data.GSTAmount, muted: true, size: 8.5f, rule: true);
        col.Item().PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text("Total").FontSize(12).SemiBold();
            r.ConstantItem(140).AlignRight().Text(Money(data.TotalFare)).FontSize(15).SemiBold();
        });

        Section(col, "Payment summary", top: 14);
        Detail(col, "Payment option", IsPartPay ? "Part payment (advance + balance)" : "Full payment");
        Detail(col, "Payment method", string.IsNullOrWhiteSpace(data.PaymentMethod) ? "Online payment" : data.PaymentMethod);
        Detail(col, "Payment status", PaymentStatusText(), bold: true);
        if (data.PaidDate is { } paidOn) Detail(col, "Paid on", $"{Date(paidOn)} {Time(paidOn)}");
        Amount(col, "Amount paid", Paid, rule: true);
        Amount(col, "Balance due", Outstanding, bold: true);
        if (IsPartPay)
            Paragraph(col, "The balance is payable as per the part-payment terms of your booking.", top: 4, muted: true);

        Section(col, "Please note");
        NoteGroup(col, "Extra charges (payable to the driver during the trip)", ExtraKmLines(data));
        NoteGroup(col, "Your trip", new[]
        {
            "Your trip includes one pick-up in the pick-up city and one drop in the destination city. Within-city travel, multiple pick-ups or drops are at the driver's discretion and charged extra.",
            "The cab AC may be switched off during hill climbs.",
            "Please check the odometer when you board and at the end of the trip, and take all belongings with you."
        });
        NoteGroup(col, "Billing", new[] { "Any discrepancy in the bill amount will be considered within 24 hours of the invoice." });

        ContactBand(col, "Should you have any questions or require further assistance, please do not hesitate to contact us. We will be happy to help.",
            data.VendorEmail, configuration["Invoice:SupportPhone"]);
        Footer(col, $"We look forward to serving you with {data.VendorName}.", brand, configuration);
    });

    private bool IsPartPay => string.Equals(data.PaymentOption, "PartPay", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mirrors the amount due at booking, as the previous invoice did.</summary>
    private decimal Paid => data.PayableNowAmount;

    private decimal Outstanding => Math.Max(0m, data.TotalFare - Paid);

    // Judged from received (successful) money only, so a partial payment is never shown as full.
    private string PaymentStatusText()
    {
        if (data.AmountPaid >= data.TotalFare && data.TotalFare > 0) return "Paid in full";
        if (data.AmountPaid > 0) return "Partially paid";
        return data.PaymentStatus?.ToLowerInvariant() switch
        {
            "failed" => "Payment failed",
            "refunded" => "Refunded",
            _ => "Payment pending"
        };
    }
}
