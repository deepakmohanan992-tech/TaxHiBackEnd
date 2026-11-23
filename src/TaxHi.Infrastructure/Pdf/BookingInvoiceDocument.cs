using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models.Booking;
using Microsoft.Extensions.Configuration;

namespace TaxHi.Infrastructure.Pdf;

/// <summary>A4 "Travel Itinerary" invoice for one booking.</summary>
public sealed class BookingInvoiceDocument(BookingInvoiceDto data, IConfiguration configuration, byte[]? logo = null) : IDocument
{
    private const string Brand = "#2E8BC9";
    private const string BrandBand = "#D9E7EC";
    private const string SectionBar = "#EFEFEF";
    private const string Muted = "#555555";
    private const string Border = "#DDDDDD";
    private const float Gutter = 40;
    private const float LabelWidth = 150;
    private const float HalfLabelWidth = 82;

    private static readonly CultureInfo Money = BuildMoneyCulture();

    // Read straight from the "Invoice" section of appsettings.json; defaults only keep the PDF from breaking.
    //private string CompanyName => configuration["Invoice:CompanyName"] is { Length: > 0 } v ? v : "TaxHi";
    private string SupportEmail => configuration["Invoice:SupportEmail"] ?? string.Empty;
    private string SupportPhone => configuration["Invoice:SupportPhone"] ?? string.Empty;
    private string CompanyAddress => configuration["Invoice:CompanyAddress"] ?? string.Empty;
    private string Website => configuration["Invoice:Website"] ?? string.Empty;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Travel Itinerary {data.BookingNo}",
        Author = data.VendorName,
        Subject = "Booking invoice"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor("#222222").LineHeight(1.3f));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingHorizontal(Gutter).PaddingTop(8).PaddingBottom(8).Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });

    // ---------------------------------------------------------------- header / footer

    private void ComposeHeader(IContainer container) =>
        container.Background(BrandBand).PaddingVertical(10).AlignCenter().Column(col =>
        {
            col.Item().AlignCenter().Element(ComposeLogo);
            col.Item().PaddingTop(4).AlignCenter().Text("Travel Itinerary")
                .FontSize(18).Bold().FontColor(Brand);
        });

    private void ComposeLogo(IContainer container)
    {
        if (logo is { Length: > 0 })
        {
            container.Height(36).Image(logo).FitHeight();
            return;
        }

        container.Background(Brand).CornerRadius(5).PaddingHorizontal(22).PaddingVertical(5)
            .Text(data.VendorName).FontSize(20).Bold().FontColor(Colors.White);
    }

    private void ComposeFooter(IContainer container) =>
        container.Column(col =>
        {
            col.Item().LineHorizontal(2).LineColor(Brand);
            col.Item().Background("#F4F7F9").PaddingHorizontal(Gutter).PaddingVertical(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("In case of any queries reach out to us at:").FontSize(8).FontColor(Muted);
                    c.Item().Text(data.VendorEmail).Bold().FontSize(9);
                    if (!string.IsNullOrWhiteSpace(SupportPhone))
                        c.Item().Text(SupportPhone).Bold().FontSize(9);
                });
                row.RelativeItem().AlignCenter().Text("24x7 Customer Support  |  Transparent Billing")
                    .FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(data.VendorName).Bold().FontSize(10).FontColor(Brand);
                    c.Item().AlignRight().Text(data.Website).FontSize(8).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(CompanyAddress))
                        c.Item().AlignRight().Text(CompanyAddress).FontSize(7).FontColor(Muted);
                });
            });
            col.Item().Background("#F4F7F9").PaddingBottom(6).AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });

    // ---------------------------------------------------------------- body

    private void ComposeBody(IContainer container) =>
        container.Column(col =>
        {
            col.Spacing(6);

            col.Item().Element(c => Section(c, "Booking Details", ComposeBookingDetails));
            col.Item().ShowEntire().Row(row =>
            {
                row.RelativeItem().Element(c => Section(c, "Customer Details", ComposeCustomerDetails));
                row.ConstantItem(10);
                row.RelativeItem().Element(c => Section(c, "Vehicle Information", ComposeVehicle));
            });
            col.Item().Element(c => Section(c, "Billing Details", ComposeBilling));
            col.Item().Element(c => Section(c, "Payment Details", ComposePayment));
            if (data.AddOns.Count > 0)
                col.Item().Element(c => Section(c, "Additional Charges & Add-ons", ComposeAddOns));
            col.Item().Element(c => Section(c, "Important T&C", ComposeTerms));
        });

    private static void Section(IContainer container, string title, Action<IContainer> body) =>
        container.ShowEntire().Column(col =>
        {
            col.Item().Background(SectionBar).PaddingVertical(5).PaddingHorizontal(6)
                .Text($"{title}:").Bold().FontSize(9.5f);
            col.Item().PaddingVertical(4).PaddingHorizontal(6).Element(body);
        });

    private void ComposeBookingDetails(IContainer container) =>
        container.Column(col =>
        {
            Row(col, "Booking ID", data.BookingNo, bold: true);
            Row(col, "Booking Date", FormatDate(data.CreatedDate));
            Row(col, "Booking Status", data.BookingStatus);
            Row(col, "Pickup Location", data.PickupLocation);
            Row(col, "Drop Location", data.DropLocation);
            Row(col, "Pickup Time", FormatDateTime(data.PickupDateTime));
            if (data.ReturnDateTime is { } ret && data.ReturnDateTime != data.PickupDateTime)
                Row(col, "Return Time", FormatDateTime(ret));
            else if (data.DropDate is { } drop)
                Row(col, "Drop Date", FormatDate(drop));
            Row(col, "Trip Detail", TripDetail());
        });

    private void ComposeCustomerDetails(IContainer container) =>
        container.Column(col =>
        {
            Row(col, "Name", data.CustomerName, labelWidth: HalfLabelWidth);
            Row(col, "Phone", data.CustomerPhone, labelWidth: HalfLabelWidth);
            Row(col, "Email", data.CustomerEmail, labelWidth: HalfLabelWidth);
            if (!string.IsNullOrWhiteSpace(data.AlternateEmail))
                Row(col, "Alternate Email", data.AlternateEmail, labelWidth: HalfLabelWidth);
            if (data.HasGST)
            {
                Row(col, "GST Number", data.GSTNumber, labelWidth: HalfLabelWidth);
                Row(col, "GST Company", data.GSTCompanyName, labelWidth: HalfLabelWidth);
            }
        });

    private void ComposeVehicle(IContainer container) =>
        container.Column(col =>
        {
            Row(col, "Car Type", string.IsNullOrWhiteSpace(data.VehicleName) ? null : $"{data.VehicleName}", labelWidth: HalfLabelWidth);
            Row(col, "Fuel Type", data.FuelType, labelWidth: HalfLabelWidth);
            // Only present once the trip is assigned to a registered vehicle / driver.
            Row(col, "Registration No", data.RegistrationNo, skipIfEmpty: true, labelWidth: HalfLabelWidth);
            Row(col, "Driver", data.DriverName, skipIfEmpty: true, labelWidth: HalfLabelWidth);
            Row(col, "Driver Contact", data.DriverMobile, skipIfEmpty: true, labelWidth: HalfLabelWidth);
        });

    private void ComposeBilling(IContainer container) =>
        container.Column(col =>
        {
            AmountRow(col, "Total Booking Amount", data.TotalFare, "(Inclusive of taxes)", bold: true);
            AmountRow(col, "a) Base Fare", data.BaseFare);
            if (data.DriverAllowance > 0)
                AmountRow(col, "b) Driver Allowance", data.DriverAllowance);
            if (data.AddOnsTotalAmount > 0)
                AmountRow(col, "c) Add-ons", data.AddOnsTotalAmount);
            if (data.DiscountAmount > 0)
                AmountRow(col, string.IsNullOrWhiteSpace(data.CouponCode) ? "d) Discount" : $"d) Discount ({data.CouponCode})",
                    -data.DiscountAmount);
            AmountRow(col, "e) GST", data.GSTAmount);

            col.Item().PaddingTop(6).PaddingVertical(2)
                .Text("Extra Charges, if applicable (to be paid to the driver during the trip):").Bold();
            foreach (var line in ExtraChargeLines())
                col.Item().PaddingVertical(2).Text(line);
        });

    // Km terms come from the vendor rate card; when unavailable the line stays generic so the PDF never breaks.
    private IEnumerable<string> ExtraChargeLines()
    {
        yield return data.IncludedKilometres is > 0 && data.ExtraKmRate is > 0
            ? $"a) Distance travelled beyond {data.IncludedKilometres.Value.ToString("0.##", CultureInfo.InvariantCulture)} km will be charged at Rs. {data.ExtraKmRate.Value.ToString("N2", Money)}/km."
            : "a) Distance travelled beyond the included kilometres will be charged at the applicable per-km rate.";
        yield return "b) This fare includes inter-state tax and toll, but does not include parking.";
        yield return "c) This fare also includes TDS charges applicable for the trip.";
    }

    private void ComposePayment(IContainer container) =>
        container.Column(col =>
        {
            Row(col, "Payment Option", PaymentOptionText());
            Row(col, "Payment Method", string.IsNullOrWhiteSpace(data.PaymentMethod) ? "Online payment" : data.PaymentMethod);
            Row(col, "Payment Status", PaymentStatusText(), bold: true);
            if (IsPartPay && data.PayableNowAmount > 0)
                AmountRow(col, "Advance Payable Now", data.PayableNowAmount);
            AmountRow(col, "Actual Amount Paid", ActualAmountPaid, bold: true);
            if (data.PaidDate is { } paidOn)
                Row(col, "Paid On", FormatDateTime(paidOn));
            AmountRow(col, "Outstanding Balance", Outstanding, IsPartPay ? "(To be paid as per part-payment terms)" : null, bold: true);
        });

    private void ComposeAddOns(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });

            table.Header(h =>
            {
                HeaderCell(h.Cell(), "Add-on", false);
                HeaderCell(h.Cell(), "Price Type", false);
                HeaderCell(h.Cell(), "Unit Price", true);
                HeaderCell(h.Cell(), "Amount", true);
            });

            foreach (var a in data.AddOns)
            {
                BodyCell(table.Cell(), a.AddOnName, false);
                BodyCell(table.Cell(), a.PriceType, false);
                BodyCell(table.Cell(), FormatMoney(a.UnitPrice), true);
                BodyCell(table.Cell(), FormatMoney(a.CalculatedAmount), true);
            }
        });

    private static void ComposeTerms(IContainer container) =>
        container.Column(col =>
        {
            col.Spacing(3);
            foreach (var line in TermsAndConditions)
                col.Item().Row(r =>
                {
                    r.ConstantItem(10).Text("•");
                    r.RelativeItem().Text(line).FontSize(8.5f);
                });
        });

    private static readonly string[] TermsAndConditions =
    [
        "Your trip has a kilometre limit. If your usage exceeds it, the excess kilometres are charged as per the applicable rate. Please check the odometer reading when you board the cab and at the end of the trip.",
        "If you have chosen to pay part of the amount, the outstanding balance is to be paid as per the part-payment terms shown above.",
        "Your trip includes one pick-up in the pick-up city and one drop in the destination city. It does not include within-city travel. Multiple pick-ups, multiple drops or within-city travel are at the driver's discretion and will be charged extra.",
        "The fare includes inter-state tax and toll unless stated otherwise, but does not include parking.",
        "If your trip has hill climbs, the cab AC may be switched off during such climbs.",
        "At the end of the trip, please check and take all your belongings with you.",
        "Any discrepancy regarding the bill amount will be considered within 24 hours of the invoice."
    ];

    // ---------------------------------------------------------------- building blocks

    private static void Row(ColumnDescriptor col, string label, string? value, bool bold = false, bool skipIfEmpty = false, float labelWidth = LabelWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (skipIfEmpty) return;
            value = "—";
        }

        col.Item().PaddingVertical(2).Row(r =>
        {
            r.ConstantItem(labelWidth).Text(label).Bold();
            var text = r.RelativeItem().Text(value);
            if (bold) text.Bold();
        });
    }

    private static void AmountRow(ColumnDescriptor col, string label, decimal amount, string? note = null, bool bold = false)
    {
        col.Item().PaddingVertical(2).Row(r =>
        {
            r.ConstantItem(LabelWidth).Text(label).Bold();
            r.RelativeItem().Text(t =>
            {
                var main = t.Span(FormatMoney(amount));
                if (bold) main.Bold();
                if (note is not null)
                    t.Span($"  {note}").FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void HeaderCell(IContainer cell, string text, bool right)
    {
        var c = cell.Background(SectionBar).BorderBottom(1).BorderColor(Border).Padding(4);
        (right ? c.AlignRight() : c).Text(text).Bold().FontSize(8.5f);
    }

    private static void BodyCell(IContainer cell, string text, bool right)
    {
        var c = cell.BorderBottom(1).BorderColor(Border).Padding(4);
        (right ? c.AlignRight() : c).Text(text).FontSize(8.5f);
    }

    // ---------------------------------------------------------------- derived values

    private bool IsPartPay => string.Equals(data.PaymentOption, "PartPay", StringComparison.OrdinalIgnoreCase);

    /// <summary>Shown as "Actual Amount Paid": mirrors the Advance Payable Now amount (the amount due at booking).</summary>
    private decimal ActualAmountPaid => data.PayableNowAmount;

    /// <summary>Total minus the amount shown as paid, so the three figures always add up; never negative.</summary>
    private decimal Outstanding => Math.Max(0m, data.TotalFare - ActualAmountPaid);

    private string PaymentOptionText() => IsPartPay ? "Part Payment (advance + balance)" : "Full Payment";

    // Paid is judged from received (successful) money only, so a partial payment is never shown as full.
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

    private string TripDetail()
    {
        var from = FirstSegment(data.FromCity) ?? FirstSegment(data.PickupLocation);
        var to = FirstSegment(data.ToCity) ?? FirstSegment(data.DropLocation);
        var type = data.TripType?.ToLowerInvariant() switch
        {
            "oneway" or "one-way" or "one way" => "One-way",
            "roundtrip" or "round-trip" or "round trip" => "Round trip",
            null or "" => null,
            _ => data.TripType
        };
        var route = $"{from} → {to}";
        return type is null ? route : $"{route} ({type})";
    }

    private static string? FirstSegment(string? place) =>
        string.IsNullOrWhiteSpace(place) ? null : place.Split(',')[0].Trim();

    // ---------------------------------------------------------------- formatting

    private static string FormatMoney(decimal amount) =>
        amount < 0
            ? $"- Rs. {Math.Abs(amount).ToString("N2", Money)}"
            : $"Rs. {amount.ToString("N2", Money)}";

    private static string? FormatDateTime(DateTime? value) =>
        value is null ? null : $"{value.Value.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant()}, {FormatDate(value.Value)}";

    private static string FormatDate(DateTime value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static CultureInfo BuildMoneyCulture()
    {
        try { return CultureInfo.GetCultureInfo("en-IN"); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
