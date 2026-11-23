using TaxHi.Application.Models.Booking;
using TaxHi.Infrastructure.Template;

namespace TaxHi.UnitTests;

public class BookingConfirmationEmailTemplateTests
{
    private static readonly BookingEmailBranding Branding =
        new("TaxHi", null, null, null, "https://taxhi.example/terms", "1800-000-000", "help@taxhi.example");

    private static BookingConfirmationEmailDto Sample() => new()
    {
        BookingNo = "S1126-13334779",
        CustomerName = "Asha <b>Nair</b>",
        CustomerPhone = "+91 98765 41154",
        TripType = "One Way",
        PickupDateTime = new DateTime(2026, 11, 15, 9, 30, 0),
        PickupCity = "Kochi",
        DropCity = "Munnar",
        PickupLocation = "Kochi Airport",
        DropLocation = "Munnar Town",
        DistanceKm = 130m,
        CarType = "Innova"
    };

    [Fact]
    public void Subject_matches_required_format() =>
        Assert.Equal("Your confirmed One Way trip booking from Kochi on 15 Nov (ID: S1126-13334779)",
            BookingConfirmationEmailTemplate.BuildSubject(Sample()));

    [Theory]
    [InlineData("9876541154", "******1154")]
    [InlineData("+91 98765 41154", "********1154")]
    [InlineData("123", "123")]
    [InlineData(null, "-")]
    public void Phone_is_masked_except_last_four(string? phone, string expected) =>
        Assert.Equal(expected, BookingConfirmationEmailTemplate.MaskPhone(phone));

    [Fact]
    public void Body_contains_summary_and_encodes_customer_input()
    {
        var html = BookingConfirmationEmailTemplate.BuildBody(Sample(), Branding);

        Assert.Contains("Kochi → Munnar (130 km One Way)", html);
        Assert.Contains("09:30 AM", html);
        Assert.Contains("YOUR SAFETY IS OUR PRIORITY!", html);
        Assert.Contains("Team TaxHi", html);
        Assert.Contains("Asha &lt;b&gt;Nair&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Nair</b>", html);
        Assert.DoesNotContain("9876541154", html);
    }
}
