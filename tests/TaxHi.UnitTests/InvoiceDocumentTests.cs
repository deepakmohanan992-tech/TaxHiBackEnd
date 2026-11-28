using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models.Booking;
using TaxHi.Infrastructure.Pdf;

namespace TaxHi.UnitTests;

public class InvoiceDocumentTests
{
    static InvoiceDocumentTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static BookingInvoiceDto Sample() => new()
    {
        BookingNo = "S1126-13334779",
        BookingStatus = "Confirmed",
        CreatedDate = new DateTime(2026, 11, 26),
        CustomerName = "Asha <b>Nair</b>",
        VendorName = "Lake Cabs",
        VendorEmail = "ops@lakecabs.example",
        TripType = "OneWay",
        FromCity = "Kochi",
        ToCity = "Munnar",
        PickupDateTime = new DateTime(2026, 12, 20, 14, 0, 0),
        DropDate = new DateTime(2026, 12, 20),
        VehicleName = "Innova",
        BaseFare = 800m,
        GSTAmount = 124m,
        TotalFare = 924m,
        PayableNowAmount = 924m,
        AmountPaid = 924m,
        AddOns = [new BookingInvoiceAddOnDto { AddOnName = "Child seat", PriceType = "Fixed", UnitPrice = 0, CalculatedAmount = 0 }]
    };

    private static bool IsPdf(byte[] bytes) => bytes.Length > 1000 && bytes[0] == '%' && bytes[1] == 'P';

    [Fact]
    public void Customer_invoice_renders_without_vehicle_image() =>
        Assert.True(IsPdf(new CustomerInvoiceDocument(Sample(), Config()).GeneratePdf()));

    [Fact]
    public void Vendor_invoice_renders_and_survives_undecodable_image() =>
        Assert.True(IsPdf(new VendorInvoiceDocument(Sample(), Config(), vehicleImage: [1, 2, 3]).GeneratePdf()));

    [Fact]
    public void Vendor_payout_defaults_to_no_fee()
    {
        var p = VendorInvoiceDocument.Payout(Sample(), Config());
        Assert.Equal(0m, p.ServiceFeeWithheld);
        Assert.Equal(924m, p.AmountPayable);
    }

    [Fact]
    public void Vendor_payout_applies_configured_fee_and_gst()
    {
        var p = VendorInvoiceDocument.Payout(Sample(),
            Config(("Invoice:VendorServiceFeePercent", "25"), ("Invoice:VendorServiceFeeGstPercent", "18")));

        Assert.Equal(800m, p.NetOfGst);
        Assert.Equal(200m, p.ServiceFee);
        Assert.Equal(36m, p.ServiceFeeGst);
        Assert.Equal(236m, p.ServiceFeeWithheld);
        Assert.Equal(688m, p.AmountPayable);
    }
}
