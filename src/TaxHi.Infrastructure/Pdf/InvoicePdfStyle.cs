using System.Globalization;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TaxHi.Application.Models.Booking;

namespace TaxHi.Infrastructure.Pdf;

/// <summary>Look and shared building blocks of the customer and vendor invoice PDFs (same visual language).</summary>
internal static class InvoicePdfStyle
{
    public const string Page = "#FFF7EA";
    public const string Card = "#FFFEFC";
    public const string Accent = "#3F6B5C";
    public const string AccentLine = "#A0D0C1";
    public const string Rule = "#EDE6DA";
    public const string Ink = "#252525";
    public const string Muted = "#7A7A7A";
    public const string Band = "#F3F8F6";

    private static readonly CultureInfo Culture = BuildCulture();

    // ------------------------------------------------------------ page frame

    /// <summary>Cream page with a centred white card; <paramref name="body"/> fills the card.</summary>
    public static void Frame(IDocumentContainer container, Action<ColumnDescriptor> body) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Page);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink).LineHeight(1.35f));
            page.Content().PaddingVertical(24).AlignCenter().Width(430).Background(Card)
                .PaddingHorizontal(34).PaddingVertical(28).Column(body);
        });

    public static void Wordmark(ColumnDescriptor col, string brand, byte[]? logo) =>
        col.Item().AlignCenter().Element(c =>
        {
            if (logo is { Length: > 0 }) c.Height(34).Image(logo).FitHeight();
            else c.Text(brand).FontSize(22).FontColor(Accent).LetterSpacing(0.04f);
        });

    public static void Eyebrow(ColumnDescriptor col, string text, bool center = true, float top = 0) =>
        col.Item().PaddingTop(top).Element(c => (center ? c.AlignCenter() : c)
            .Text(text.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Accent).LetterSpacing(0.12f));

    public static void Heading(ColumnDescriptor col, string text, float size = 19) =>
        col.Item().PaddingTop(6).AlignCenter().Text(text).FontSize(size).FontColor(Ink).AlignCenter();

    /// <summary>Vehicle photo, or a quiet placeholder band naming the vehicle when there is none / it is unreadable.</summary>
    public static void Hero(ColumnDescriptor col, byte[]? image, string? caption)
    {
        col.Item().PaddingTop(16).Element(c =>
        {
            if (image is { Length: > 0 })
            {
                QuestPDF.Infrastructure.Image? decoded = null;
                try { decoded = QuestPDF.Infrastructure.Image.FromBinaryData(image); }
                catch { /* not a decodable image: fall through to the placeholder */ }
                if (decoded is not null) { c.Height(150).Image(decoded).FitArea(); return; }
            }

            c.Height(80).Background(Band).AlignCenter().AlignMiddle()
                .Text(string.IsNullOrWhiteSpace(caption) ? "Vehicle" : caption).FontSize(11).FontColor(Accent);
        });
    }

    /// <summary>Two-cell band between hairlines (pickup | drop).</summary>
    public static void DateBand(ColumnDescriptor col, string leftLabel, DateTime? left, string rightLabel, DateTime? right, string? footnote = null) =>
        col.Item().PaddingTop(18).BorderTop(0.75f).BorderBottom(0.75f).BorderColor(AccentLine).Column(c =>
        {
            c.Item().Row(r =>
            {
                r.RelativeItem().Element(x => DateCell(x, leftLabel, left));
                r.ConstantItem(0.75f).Background(Rule);
                r.RelativeItem().Element(x => DateCell(x, rightLabel, right));
            });
            if (!string.IsNullOrWhiteSpace(footnote))
                c.Item().PaddingBottom(8).AlignCenter().Text(footnote).FontSize(8).FontColor(Muted);
        });

    private static void DateCell(IContainer c, string label, DateTime? value) =>
        c.PaddingVertical(12).Column(k =>
        {
            k.Item().AlignCenter().Text(label.ToUpperInvariant()).FontSize(7).Bold().FontColor(Accent).LetterSpacing(0.12f);
            k.Item().PaddingTop(4).AlignCenter().Text(value is { } v ? Date(v) : "—").FontSize(14).FontColor(Ink);
            k.Item().PaddingTop(3).AlignCenter().Text(value is { } t ? Time(t) : "").FontSize(8.5f).FontColor(Muted);
        });

    public static void Section(ColumnDescriptor col, string title, float top = 22) =>
        col.Item().PaddingTop(top).PaddingBottom(6).Text(title.ToUpperInvariant())
            .FontSize(7.5f).Bold().FontColor(Accent).LetterSpacing(0.12f);

    /// <summary>One "label ... value" line with a hairline; skipped (or dashed) when the value is empty.</summary>
    public static void Detail(ColumnDescriptor col, string label, string? value, bool bold = false, bool skipIfEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (skipIfEmpty) return;
            value = "—";
        }

        col.Item().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).Row(r =>
        {
            r.RelativeItem(44).Text(label).FontSize(9).FontColor(Muted);
            var t = r.RelativeItem(56).Text(value).FontSize(9);
            if (bold) t.SemiBold();
        });
    }

    /// <summary>Right-aligned money line; negative amounts print as "- INR x".</summary>
    public static void Amount(ColumnDescriptor col, string label, decimal amount, bool bold = false, bool muted = false, float size = 9, bool rule = false) =>
        col.Item().Element(c =>
        {
            if (rule) c = c.BorderBottom(0.5f).BorderColor(Rule);
            c.PaddingVertical(4).Row(r =>
            {
                var l = r.RelativeItem().Text(label).FontSize(size);
                var v = r.ConstantItem(120).AlignRight().Text(Money(amount)).FontSize(size);
                if (bold) { l.SemiBold(); v.SemiBold(); }
                if (muted) { l.FontColor(Muted); v.FontColor(Muted); }
            });
        });

    public static void Paragraph(ColumnDescriptor col, string text, float top = 8, bool muted = false) =>
        col.Item().PaddingTop(top).Text(text).FontSize(9).FontColor(muted ? Muted : Ink);

    public static void NoteGroup(ColumnDescriptor col, string title, IEnumerable<string> lines)
    {
        col.Item().PaddingTop(8).Text(title).FontSize(9).SemiBold();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            col.Item().PaddingTop(2).Row(r =>
            {
                r.ConstantItem(10).Text("•").FontColor(Muted);
                r.RelativeItem().Text(line).FontSize(8.5f).FontColor(Muted);
            });
    }

    public static void ContactBand(ColumnDescriptor col, string message, string? email, string? phone) =>
        col.Item().PaddingTop(20).Background(Band).Padding(14).Column(c =>
        {
            c.Item().Text(message).FontSize(9);
            var contact = string.Join("   ·   ", new[] { email, phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (contact.Length > 0)
                c.Item().PaddingTop(6).Text(contact).FontSize(8.5f).FontColor(Accent);
        });

    public static void Footer(ColumnDescriptor col, string closing, string brand, IConfiguration cfg)
    {
        col.Item().PaddingTop(24).AlignCenter().Text(closing).FontSize(9);
        var lines = new[] { brand, cfg["Invoice:CompanyAddress"], cfg["Invoice:Website"] }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        col.Item().PaddingTop(12).AlignCenter().Text(string.Join("\n", lines)).FontSize(7.5f).FontColor(Muted).AlignCenter();
    }

    // ------------------------------------------------------------ shared wording / data helpers

    public static string Brand(IConfiguration cfg) =>
        cfg["Invoice:CompanyName"] is { Length: > 0 } v ? v : "TaxHi";

    public static string? FirstSegment(string? place) =>
        string.IsNullOrWhiteSpace(place) ? null : place.Split(',')[0].Trim();

    public static string Route(BookingInvoiceDto d) =>
        $"{FirstSegment(d.FromCity) ?? FirstSegment(d.PickupLocation) ?? "—"} → {FirstSegment(d.ToCity) ?? FirstSegment(d.DropLocation) ?? "—"}";

    public static string? TripTypeText(string? tripType) => tripType?.Trim().ToLowerInvariant() switch
    {
        "oneway" or "one-way" or "one way" => "One-way",
        "roundtrip" or "round-trip" or "round trip" => "Round trip",
        null or "" => null,
        _ => tripType
    };

    public static string TripDetail(BookingInvoiceDto d) =>
        TripTypeText(d.TripType) is { } type ? $"{Route(d)} ({type})" : Route(d);

    /// <summary>Drop-off moment: the return time on round trips, else the drop date.</summary>
    public static DateTime? DropMoment(BookingInvoiceDto d) =>
        d.ReturnDateTime is { } r && r != d.PickupDateTime ? r : d.DropDate;

    public static string Money(decimal amount)
    {
        var text = Math.Abs(amount).ToString(amount % 1 == 0 ? "N0" : "N2", Culture);
        return amount < 0 ? $"- INR {text}" : $"INR {text}";
    }

    public static string Date(DateTime v) => v.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string Time(DateTime v) => v.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    public static string LongDate(DateTime v) => v.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture);

    public static IEnumerable<string> ExtraKmLines(BookingInvoiceDto d)
    {
        yield return d.IncludedKilometres is > 0 && d.ExtraKmRate is > 0
            ? $"Distance beyond {d.IncludedKilometres.Value.ToString("0.##", CultureInfo.InvariantCulture)} km is charged at INR {d.ExtraKmRate.Value.ToString("N2", Culture)} per km."
            : "Distance beyond the included kilometres is charged at the applicable per-km rate.";
        yield return "The fare includes inter-state tax and toll, but not parking.";
    }

    private static CultureInfo BuildCulture()
    {
        try { return CultureInfo.GetCultureInfo("en-IN"); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
