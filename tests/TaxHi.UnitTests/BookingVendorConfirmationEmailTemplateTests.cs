using TaxHi.Application.Models.Booking;
using TaxHi.Infrastructure.Template;

namespace TaxHi.UnitTests;

public class BookingVendorConfirmationEmailTemplateTests
{
    private static readonly BookingEmailBranding Branding =
        new("TaxHi", null, null, null, "https://taxhi.example/terms", "1800-000-000", "help@taxhi.example");

    private static BookingConfirmationEmailDto Sample() => new()
    {
        BookingNo = "S1126-13334779",
        VendorName = "Lake Cabs",
        CustomerName = "Asha <b>Nair</b>",
        CustomerPhone = "+91 98765 41154",
        TripType = "One Way",
        PickupDateTime = new DateTime(2026, 11, 15, 9, 30, 0),
        PickupCity = "Kochi",
        DropCity = "Munnar",
        PickupLocation = "Kochi Airport",
        DropLocation = "Munnar Town",
        CarType = "Innova"
    };

    [Fact]
    public void Subject_names_booking_and_pickup() =>
        Assert.Equal("New booking confirmed: One Way trip from Kochi on 15 Nov (ID: S1126-13334779)",
            BookingVendorConfirmationEmailTemplate.BuildSubject(Sample()));

    [Fact]
    public void Body_shows_guest_contact_and_encodes_input()
    {
        var html = BookingVendorConfirmationEmailTemplate.BuildBody(Sample(), Branding);

        Assert.Contains("S1126-13334779", html);
        Assert.Contains("+91 98765 41154", html);
        Assert.Contains("Kochi → Munnar (One Way)", html);
        Assert.Contains("Innova", html);
        Assert.Contains("help@taxhi.example", html);
        Assert.Contains("Asha &lt;b&gt;Nair&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Nair</b>", html);
    }

    [Fact]
    public void Body_survives_missing_data() =>
        Assert.Contains("A customer", BookingVendorConfirmationEmailTemplate.BuildBody(
            new BookingConfirmationEmailDto { BookingNo = "X1" }, Branding));
}
