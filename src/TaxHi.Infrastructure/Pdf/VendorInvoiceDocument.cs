using System.Globalization;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models.Booking;
using static TaxHi.Infrastructure.Pdf.InvoicePdfStyle;

namespace TaxHi.Infrastructure.Pdf;

/// <summary>
/// Vendor-facing booking statement ("New booking"): trip, fare breakdown and the vendor payout after the
/// platform service fee. The fee rate is configuration (<c>Invoice:VendorServiceFeePercent</c>,
/// <c>Invoice:VendorServiceFeeGstPercent</c>); with no rate configured the fee is zero.
/// </summary>
public sealed class VendorInvoiceDocument(
    BookingInvoiceDto data, IConfiguration configuration, byte[]? logo = null, byte[]? vehicleImage = null) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Vendor Booking Statement {data.BookingNo}",
        Author = Brand(configuration),
        Subject = "Vendor booking invoice"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container) => Frame(container, col =>
    {
        var brand = Brand(configuration);
        var p = Payout(data, configuration);

        Wordmark(col, brand, logo);
        Eyebrow(col, "New booking", top: 22);
        var customer = string.IsNullOrWhiteSpace(data.CustomerName) ? "A customer" : data.CustomerName;
        var when = data.PickupDateTime is { } pick ? $" picks up on {LongDate(pick)}." : " has booked a trip.";
        Heading(col, $"New booking confirmed! {customer}{when}", 16);
        if (TripTypeText(data.TripType) is { } type)
            col.Item().PaddingTop(4).AlignCenter().Text(type).FontSize(8.5f).FontColor(Muted);
        Hero(col, vehicleImage, data.VehicleName);
        DateBand(col, "Pickup", data.PickupDateTime, "Drop", DropMoment(data), Route(data));

        Paragraph(col, $"For any amendments, pickup updates, special requests or payment-related queries regarding this booking, please contact {brand} Reservations. All customer communication for this booking will be managed by our reservations team.", top: 18, muted: true);

        Section(col, "Booking details");
        Detail(col, "Booking reference", data.BookingNo, bold: true);
        Detail(col, "Guest", data.CustomerName, bold: true);
        Detail(col, "Trip", TripDetail(data));
        Detail(col, "Pickup location", data.PickupLocation);
        Detail(col, "Drop location", data.DropLocation);
        Detail(col, "Vehicle", data.VehicleName, bold: true);
        Detail(col, "Fuel type", data.FuelType, skipIfEmpty: true);
        Detail(col, "Registration no", data.RegistrationNo, skipIfEmpty: true);
        Detail(col, "Driver", data.DriverName, skipIfEmpty: true);
        Detail(col, "Booked on", Date(data.CreatedDate));
        Detail(col, "Status", data.BookingStatus);

        Section(col, "Fare breakdown", top: 26);
        Amount(col, "Base fare", data.BaseFare, rule: true);
        if (data.DriverAllowance > 0) Amount(col, "Driver allowance", data.DriverAllowance, rule: true);
        if (data.AddOnsTotalAmount > 0) Amount(col, "Add-ons", data.AddOnsTotalAmount, rule: true);
        foreach (var a in data.AddOns)
            Amount(col, $"   {a.AddOnName}", a.CalculatedAmount, muted: true, size: 8.5f, rule: true);
        if (data.DiscountAmount > 0)
            Amount(col, string.IsNullOrWhiteSpace(data.CouponCode) ? "Discount" : $"Discount ({data.CouponCode})", -data.DiscountAmount, rule: true);
        Amount(col, "GST", data.GSTAmount, rule: true);
        Amount(col, "Trip total", data.TotalFare, bold: true);

        Section(col, "Your payout for this trip", top: 26);
        col.Item().Text("1  Booking value").FontSize(9);
        Amount(col, "Fare before GST", p.NetOfGst, muted: true, size: 8.5f, rule: true);
        Amount(col, "GST", data.GSTAmount, muted: true, size: 8.5f, rule: true);
        Amount(col, "Booking value", p.BookingValue, bold: true);

        col.Item().PaddingTop(10).Text($"2  {brand} service fee").FontSize(9);
        Amount(col, $"Service fee ({p.FeePercent:0.##}% of fare)", p.ServiceFee, muted: true, size: 8.5f, rule: true);
        Amount(col, $"GST on service fee ({p.FeeGstPercent:0.##}%)", p.ServiceFeeGst, muted: true, size: 8.5f, rule: true);
        Amount(col, "Service fee withheld", p.ServiceFeeWithheld, bold: true);

        col.Item().PaddingTop(10).Text("3  Amount payable").FontSize(9);
        Amount(col, "Booking value", p.BookingValue, muted: true, size: 8.5f, rule: true);
        Amount(col, "Less: service fee withheld", -p.ServiceFeeWithheld, muted: true, size: 8.5f, rule: true);
        col.Item().PaddingTop(8).Row(r =>
        {
            r.RelativeItem().Text($"Amount {brand} pays you").FontSize(11).SemiBold();
            r.ConstantItem(140).AlignRight().Text(Money(p.AmountPayable)).FontSize(14).SemiBold();
        });

        Section(col, "Please note");
        NoteGroup(col, "Trip terms", ExtraKmLines(data).Concat(new[]
        {
            "One pick-up and one drop only; within-city travel and additional stops are charged extra.",
            "Please share the allotted vehicle and driver details with reservations before pick-up."
        }));

        ContactBand(col, "Should you have any questions or require further assistance, please do not hesitate to contact us. We will be happy to help.",
            configuration["Invoice:SupportEmail"], configuration["Invoice:SupportPhone"]);
        Footer(col, $"Thank you for partnering with {brand}.", brand, configuration);
    });

    /// <summary>Payout arithmetic, kept separate so it can be unit tested.</summary>
    public sealed record PayoutSummary(
        decimal NetOfGst, decimal BookingValue, decimal FeePercent, decimal FeeGstPercent,
        decimal ServiceFee, decimal ServiceFeeGst, decimal ServiceFeeWithheld, decimal AmountPayable);

    public static PayoutSummary Payout(BookingInvoiceDto d, IConfiguration cfg)
    {
        decimal Percent(string key, decimal fallback) =>
            decimal.TryParse(cfg[$"Invoice:{key}"], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

        var feePercent = Math.Min(Percent("VendorServiceFeePercent", 0m), 100m);
        var feeGstPercent = Percent("VendorServiceFeeGstPercent", 18m);

        var bookingValue = d.TotalFare;
        var netOfGst = Math.Max(0m, bookingValue - d.GSTAmount);
        var fee = Math.Round(netOfGst * feePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var feeGst = Math.Round(fee * feeGstPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var withheld = fee + feeGst;
        return new PayoutSummary(netOfGst, bookingValue, feePercent, feeGstPercent, fee, feeGst, withheld,
            Math.Max(0m, bookingValue - withheld));
    }
}
