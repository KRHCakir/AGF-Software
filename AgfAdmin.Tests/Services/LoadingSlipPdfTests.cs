using AgfAdmin.Models;
using AgfAdmin.Services;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace AgfAdmin.Tests.Services;

// Documenten deel 1: de laadbon als PDF. De tekst wordt uit de PDF gelezen: Engelse kopteksten, de kop en het totaal, pallets op twee
// decimalen, klant en leverancier volgens de instelling, emballage met alleen het aantal, en nergens een prijs, bedrag of btw.
public class LoadingSlipPdfTests
{
    static LoadingSlipPdfTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static LoadingSlipSnapshot Slip(bool showCustomer = true, bool showSupplier = true, int extraRows = 0, string warehouse = null)
    {
        var rows = new List<LoadingSlipRow>
        {
            new(504m, "colli", 2m, "Avocado", "Hass", "4 kg box", "Westfalia", "18", "PE", 2016m, "Kwekerij Voorbeeld B.V. · 2611001", "P-000012"),
            new(240m, "colli", 240m / 252m, "Avocado", "Hass", "4 kg box", "Westfalia", "20", "PE", 960m, "Fruitimport Zuid · 2611004", "P-000015"),
            new(120m, "colli", 120m / 80m, "Mango", "Kent", "12x125 gr", null, null, "BR", null, null, null),
        };
        for (var i = 0; i < extraRows; i++) rows.Add(new(10m, "colli", 10m / 80m, "Mango", "Kent", "12x125 gr", null, null, "BR", 40m, "Fruitimport Zuid · 2611004", $"P-9{i:000}"));
        var pallets = rows.Sum(r => r.Pallets ?? 0m);
        return new LoadingSlipSnapshot("en", showCustomer, showSupplier, "Kwekerij Voorbeeld B.V.", "Loodsweg 1, 2991 Barendrecht", null, warehouse,
            showCustomer ? "Groothandel Noord" : null, "261140", "VO-2026-0012", "PO-778", new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc), null, rows, new List<LoadingSlipPackagingRow> { new(3, "Euro-pallet") },
            rows.Sum(r => r.Quantity), pallets, rows.Where(r => r.NetKg != null).Sum(r => r.NetKg), new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc), "Jan");
    }

    private static readonly SalesOrderDocument Document = new() { Number = "LB-2026-0012", Version = 2, CreatedAt = new DateTime(2026, 11, 12, 9, 5, 0, DateTimeKind.Utc) };

    private static (string Text, int Pages) Read(LoadingSlipSnapshot slip)
    {
        using var pdf = PdfDocument.Open(LoadingSlipPdf.Render(Document, slip));
        return (string.Join("\n", pdf.GetPages().Select(p => p.Text)), pdf.NumberOfPages);
    }

    [Fact]
    public void The_pdf_has_the_english_columns_the_header_and_the_totals_and_never_a_price()
    {
        var (text, pages) = Read(Slip());
        Assert.Equal(1, pages);
        foreach (var heading in new[] { "Laadbon", "Quantity", "Pallet", "Description", "Variety", "Packaging", "Brand", "Size", "Origin", "Net kg", "Supplier",
            "Customer", "Groothandel Noord", "Position", "261140", "VO-2026-0012", "Load on", "13-11-2026 06:30", "License plate", "Warehouse", "PO-778" })
        {
            Assert.Contains(heading, text);
        }
        // Pallets op twee decimalen (240 colli bij 252 per pallet = 0,95), het totaal colli en pallets onderaan.
        Assert.Contains("0,95", text);
        Assert.Contains("864 packages", text);
        Assert.Contains($"{PurchaseLineMath.Round2(2m + 240m / 252m + 1.5m):N2}".Replace(".", ","), text);
        // Leverancier en inkooppartij (de cel loopt over twee regels, de tekst blijft vindbaar).
        Assert.Contains("2611001", text);
        Assert.Contains("Fruitimport Zuid", text);
        Assert.Contains("not reserved yet", text);
        Assert.Contains("3 × Euro-pallet", text);
        foreach (var word in new[] { "€", "EUR", "Price", "Prijs", "Amount", "Bedrag", "VAT", "btw" }) Assert.DoesNotContain(word, text);
        Assert.Equal("Laadbon LB-2026-0012 v2.pdf", LoadingSlipPdf.FileName(Document));
    }

    [Fact]
    public void The_warehouse_is_the_company_address_unless_another_loading_address_is_set()
    {
        // Het bedrijfsadres staat altijd bovenaan; als loods alleen als er geen ander laadadres is ingesteld.
        static int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
        Assert.Equal(2, Count(Read(Slip()).Text, "Loodsweg 1"));
        var (text, _) = Read(Slip(warehouse: "Koelhuis Zuid, Havenweg 4, Ridderkerk"));
        Assert.Contains("Koelhuis Zuid, Havenweg 4, Ridderkerk", text);
        Assert.Equal(1, Count(text, "Loodsweg 1"));
    }

    [Fact]
    public void A_net_kg_total_with_rows_without_a_weight_says_it_is_incomplete()
    {
        // De mango is nog niet gereserveerd en heeft dus geen gewicht: het totaal is alleen dat van de andere regels.
        Assert.Contains("net kg of the rows with a weight only", Read(Slip()).Text);
        var weighed = Slip() with { Rows = Slip().Rows.Where(r => r.NetKg != null).ToList() };
        Assert.DoesNotContain("with a weight only", Read(weighed).Text);
    }

    [Fact]
    public void Pallets_have_two_decimals_like_the_order_line_never_whole_pallets()
    {
        // Zoals de orderregel: twee decimalen, half van nul af, nooit naar hele pallets.
        Assert.Equal("0,96", LoadingSlipPdf.Pallets(0.959m));
        Assert.Equal("23,81", LoadingSlipPdf.Pallets(23.814m));
        Assert.Equal("2,08", LoadingSlipPdf.Pallets(2.08m));
    }

    [Fact]
    public void Customer_and_supplier_column_follow_the_setting_and_an_empty_plate_is_a_line_to_fill_in()
    {
        var (text, _) = Read(Slip(showCustomer: false, showSupplier: false));
        Assert.DoesNotContain("Groothandel Noord", text);
        Assert.DoesNotContain("Customer", text);
        Assert.DoesNotContain("Supplier", text);
        Assert.DoesNotContain("2611001", text);
        Assert.Contains("____", text);
    }

    [Fact]
    public void A_long_slip_runs_over_pages_with_the_heading_repeated_and_the_total_once_at_the_end()
    {
        var (text, pages) = Read(Slip(extraRows: 80));
        Assert.True(pages >= 2, $"pages: {pages}");
        Assert.Equal(pages, text.Split("Net kg").Length - 1);
        Assert.Equal(1, text.Split("packages ·").Length - 1);
    }
}
