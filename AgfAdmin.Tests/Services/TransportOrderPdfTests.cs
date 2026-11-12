using AgfAdmin.Models;
using AgfAdmin.Services;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace AgfAdmin.Tests.Services;

// Documenten deel 2: de transportopdracht als PDF. De tekst wordt uit de PDF gelezen: de velden zoals het voorbeeld ("Transport Order"),
// palletplaatsen op twee decimalen met het totaal, bruto kg, emballage met alleen het aantal, en nergens een prijs, bedrag of tarief.
public class TransportOrderPdfTests
{
    static TransportOrderPdfTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static TransportOrderSnapshot Order(bool own = false, string language = "en", int extraRows = 0)
    {
        var rows = new List<TransportOrderRow>
        {
            new(240m, "colli", "Avocado · Hass · 4 kg box", "I", "PE", 240m / 252m, 1032m),
            new(504m, "colli", "Blauwe bes · Duke · 12x125 gr", "I", "MA", 504m / 216m, 806.4m),
            new(52m, "colli", "Mango · Kent · 4 kg box", null, "BR", 52m / 25m, null),
        };
        for (var i = 0; i < extraRows; i++) rows.Add(new(10m, "colli", $"Mango · Kent · regel {i}", "II", "BR", 10m / 80m, 43m));
        var spaces = rows.Sum(r => r.PalletSpaces ?? 0m);
        return new TransportOrderSnapshot(language, "Kwekerij Voorbeeld B.V.", "Loodsweg 1, 2991 Barendrecht", null,
            own, own ? null : "Transport Zeeland B.V.", own ? null : "Kade 1, 4382 AA Vlissingen, NL",
            "VO-2026-0012", "Groothandel Noord", "Koelhuis Zuid, Havenweg 4, Ridderkerk", "Groothandel Noord, DC Noord, Havenweg 4, 9723 AB Groningen, NL",
            new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), 2m,
            "LR-55", "PO-778", "Achterom melden bij de portier", "Europallet + Corners + Straps",
            rows, new List<LoadingSlipPackagingRow> { new(4, "Euro-pallet") }, rows.Sum(r => r.Quantity), spaces, rows.Where(r => r.GrossKg != null).Sum(r => r.GrossKg),
            new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc), "Jan");
    }

    private static readonly SalesOrderDocument Document = new() { Kind = SalesOrderDocumentRules.KindTransportOrder, Number = "TO-2026-0012", Version = 1, CreatedAt = new DateTime(2026, 11, 12, 9, 5, 0, DateTimeKind.Utc) };

    private static (string Text, int Pages) Read(TransportOrderSnapshot order)
    {
        using var pdf = PdfDocument.Open(TransportOrderPdf.Render(Document, order));
        return (string.Join("\n", pdf.GetPages().Select(p => p.Text)), pdf.NumberOfPages);
    }

    [Fact]
    public void The_pdf_has_the_fields_of_the_example_and_never_a_price()
    {
        var (text, pages) = Read(Order());
        Assert.Equal(1, pages);
        foreach (var part in new[] { "Transport Order", "TO-2026-0012", "Transporter", "Transport Zeeland B.V.", "Kade 1", "Date", "12-11-2026", "Our reference",
            "Please report with", "Order number", "VO-2026-0012", "Loading reference", "LR-55", "Loading date", "13-11-2026", "Loading time", "06:30",
            "Temperature", "2 °C", "Unloading reference", "PO-778", "Unloading date", "14-11-2026", "Loading place", "Koelhuis Zuid", "Unloading place", "Groningen",
            "Quantity", "Description", "Class", "Origin", "Pallets", "Pallet space(s)", "Gross kg", "Europallet + Corners + Straps",
            "Remarks", "Achterom melden", "Please report when loading.", "4 × Euro-pallet" })
        {
            Assert.Contains(part, text);
        }
        // Palletplaatsen op twee decimalen (240 bij 252 = 0,95; 504 bij 216 = 2,33; 52 bij 25 = 2,08) en het totaal van de exacte waarden (5,37).
        foreach (var number in new[] { "0,95", "2,33", "2,08", "5,37", "1.032", "806,4", "796 packages" }) Assert.Contains(number, text);
        // Een regel zonder bruto: het totaal zegt dat het onvolledig is.
        Assert.Contains("gross kg of the rows with a weight only", text);
        foreach (var word in new[] { "€", "EUR", "Price", "Prijs", "Amount", "Bedrag", "VAT", "btw", "Tarief", "Rate", "Invoice" }) Assert.DoesNotContain(word, text);
        Assert.Equal("Transportopdracht TO-2026-0012 v1.pdf", TransportOrderPdf.FileName(Document));
    }

    [Fact]
    public void Own_transport_and_dutch_headings()
    {
        var (text, _) = Read(Order(own: true, language: "nl"));
        foreach (var part in new[] { "Transportopdracht", "Vervoerder", "Eigen transport / klant haalt op", "Melden met", "Laadplaats", "Losplaats", "Palletplaats(en)",
            "Bruto kg", "Gelieve te melden bij het laden." })
        {
            Assert.Contains(part, text);
        }
        Assert.DoesNotContain("Transport Zeeland", text);
    }

    [Fact]
    public void A_long_order_runs_over_pages_with_the_heading_repeated_and_the_total_once_at_the_end()
    {
        var (text, pages) = Read(Order(extraRows: 90));
        Assert.True(pages >= 2, $"expected several pages, got {pages}");
        static int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
        Assert.Equal(pages, Count(text, "Gross kg"));
        Assert.Equal(1, Count(text, "Total:"));
    }
}
