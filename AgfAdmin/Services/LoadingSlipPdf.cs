using System.Globalization;
using AgfAdmin.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgfAdmin.Services
{
    // De laadbon als PDF (documenten deel 1), altijd uit de snapshot van één versie: een oude versie ziet er later precies zo uit. A4, zwart-wit
    // leesbaar, afwisselende rijkleur, de kop van de tabel herhaald op elke pagina, het totaal alleen aan het eind. Kopteksten in de taal van
    // de bon (standaard Engels, de loods werkt internationaal); getallen in Nederlandse notatie (6.890 colli, 23,81 pallets), zoals het
    // voorbeeld. Bewust geen prijs, bedrag of btw, ook niet in de metadata of de bestandsnaam.
    public static class LoadingSlipPdf
    {
        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        public static byte[] Render(SalesOrderDocument document, LoadingSlipSnapshot slip) => new Slip(document, slip).GeneratePdf();

        public static string FileName(SalesOrderDocument document) => $"Laadbon {document.Number} v{document.Version}.pdf";

        private static readonly Dictionary<string, Dictionary<string, string>> Labels = new()
        {
            [SalesOrderDocumentRules.LanguageEnglish] = new()
            {
                ["customer"] = "Customer", ["position"] = "Position", ["order"] = "Sales order", ["loadOn"] = "Load on", ["plate"] = "License plate",
                ["warehouse"] = "Warehouse", ["reference"] = "Reference", ["quantity"] = "Quantity", ["pallet"] = "Pallet", ["description"] = "Description",
                ["variety"] = "Variety", ["packaging"] = "Packaging", ["brand"] = "Brand", ["size"] = "Size", ["origin"] = "Origin", ["netKg"] = "Net kg",
                ["supplier"] = "Supplier", ["total"] = "Total", ["packages"] = "packages", ["pallets"] = "pallets", ["material"] = "Packaging material going along",
                ["driver"] = "Driver name", ["signature"] = "Signature", ["date"] = "Date", ["remarks"] = "Remarks", ["version"] = "version", ["page"] = "page",
                ["created"] = "made", ["notReserved"] = "not reserved yet",
            },
            [SalesOrderDocumentRules.LanguageDutch] = new()
            {
                ["customer"] = "Klant", ["position"] = "Positie", ["order"] = "Verkooporder", ["loadOn"] = "Te laden op", ["plate"] = "Kenteken",
                ["warehouse"] = "Loods", ["reference"] = "Referentie", ["quantity"] = "Aantal", ["pallet"] = "Pallet", ["description"] = "Omschrijving",
                ["variety"] = "Ras", ["packaging"] = "Inhoud", ["brand"] = "Merk", ["size"] = "Maat", ["origin"] = "Herkomst", ["netKg"] = "Netto kg",
                ["supplier"] = "Leverancier", ["total"] = "Totaal", ["packages"] = "colli", ["pallets"] = "pallets", ["material"] = "Emballage die mee gaat",
                ["driver"] = "Naam chauffeur", ["signature"] = "Handtekening", ["date"] = "Datum", ["remarks"] = "Opmerkingen", ["version"] = "versie", ["page"] = "pagina",
                ["created"] = "gemaakt", ["notReserved"] = "nog niet gereserveerd",
            },
        };

        public static string Quantity(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "#,##0.###", Dutch);
        // Twee decimalen, nooit naar hele pallets ("niet afgerond": 2,08 blijft 2,08). Bewust dezelfde berekening als de orderregel
        // (PurchaseLineMath.Round2), zodat de laadbon hetzelfde aantal pallets toont als de order zelf; afkappen zou 0,96 op de order en 0,95
        // op de bon geven.
        public static string Pallets(decimal? value) => value == null ? "–" : PurchaseLineMath.Round2(value.Value).ToString("N2", Dutch);
        public static string Kg(decimal? value) => value == null ? "–" : value.Value.ToString("#,##0.#", Dutch);

        private static string LocalTime(DateTime utc, bool withTime) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), AmsterdamBusinessCalendar.Zone).ToString(withTime ? "dd-MM-yyyy HH:mm" : "dd-MM-yyyy", Dutch);

        private sealed class Slip(SalesOrderDocument document, LoadingSlipSnapshot slip) : IDocument
        {
            private string L(string key) => (Labels.GetValueOrDefault(slip.Language) ?? Labels[SalesOrderDocumentRules.LanguageEnglish])[key];

            public DocumentMetadata GetMetadata() => new() { Title = $"Laadbon {document.Number} v{document.Version}", Author = slip.CompanyName, Subject = slip.Position ?? slip.OrderNumber };

            public void Compose(IDocumentContainer container) => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                // Zonder ligaturen: anders worden "ti" en "ft" één teken en vindt zoeken of kopiëren in de PDF "Quantity" niet meer.
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));
                page.Header().Element(Header);
                page.Content().PaddingTop(8).Element(Content);
                page.Footer().AlignRight().Text(x =>
                {
                    x.Span($"Laadbon {document.Number} · {L("version")} {document.Version} · {L("created")} {LocalTime(document.CreatedAt, true)} · {L("page")} ").FontSize(7);
                    x.CurrentPageNumber().FontSize(7);
                    x.Span(" / ").FontSize(7);
                    x.TotalPages().FontSize(7);
                });
            });

            private void Header(IContainer container) => container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(company =>
                    {
                        // Het logo komt uit de snapshot (bytes van het moment van die versie), niet uit een bestand dat later kan veranderen.
                        if (!string.IsNullOrWhiteSpace(slip.LogoImage)) company.Item().Width(110).Image(Convert.FromBase64String(slip.LogoImage));
                        company.Item().Text(slip.CompanyName).Bold().FontSize(11);
                        if (slip.CompanyAddress != null) company.Item().Text(slip.CompanyAddress).FontSize(8);
                    });
                    row.ConstantItem(200).AlignRight().Column(title =>
                    {
                        title.Item().AlignRight().Text("Laadbon").Bold().FontSize(20);
                        title.Item().AlignRight().Text($"{document.Number} · {L("version")} {document.Version}").FontSize(8);
                    });
                });
                column.Item().PaddingTop(6).BorderTop(1).PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(80); c.RelativeColumn(); c.ConstantColumn(80); c.RelativeColumn(); });
                    void Field(string label, string value, bool bold = false)
                    {
                        table.Cell().PaddingVertical(2).Text(label).FontSize(8).FontColor(Colors.Grey.Darken3);
                        var text = table.Cell().PaddingVertical(2).Text(value ?? "");
                        if (bold) text.Bold().FontSize(11);
                    }
                    if (slip.ShowCustomer) Field(L("customer"), slip.CustomerName, true);
                    Field(L("position"), slip.Position ?? slip.OrderNumber, true);
                    Field(L("order"), slip.OrderNumber);
                    Field(L("loadOn"), slip.LoadAt != null ? LocalTime(slip.LoadAt.Value, true) : LocalTime(slip.RequestedDeliveryDate, false), true);
                    // Kenteken: leeg = een lijn om met de hand in te vullen.
                    Field(L("plate"), slip.LicensePlate ?? "____________________", slip.LicensePlate != null);
                    Field(L("warehouse"), slip.CompanyAddress == null ? slip.CompanyName : $"{slip.CompanyName}, {slip.CompanyAddress}");
                    if (slip.CustomerReference != null) Field(L("reference"), slip.CustomerReference);
                    // Even velden per rij: een lege cel aanvullen, zodat de tabel altijd sluit.
                    var fields = (slip.ShowCustomer ? 1 : 0) + 5 + (slip.CustomerReference != null ? 1 : 0);
                    if (fields % 2 == 1) { table.Cell(); table.Cell(); }
                });
            });

            private void Content(IContainer container) => container.Column(column =>
            {
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(48); c.ConstantColumn(40); c.RelativeColumn(1.4f); c.RelativeColumn(1f); c.RelativeColumn(1.1f); c.RelativeColumn(0.9f);
                        c.ConstantColumn(32); c.ConstantColumn(34); c.ConstantColumn(44);
                        if (slip.ShowSupplier) c.RelativeColumn(1.4f);
                    });
                    table.Header(header =>
                    {
                        IContainer Head(IContainer cell) => cell.Background(Colors.Grey.Lighten2).BorderBottom(1).PaddingVertical(3).PaddingHorizontal(2);
                        foreach (var key in Columns()) header.Cell().Element(Head).Text(L(key)).Bold().FontSize(8);
                    });
                    var index = 0;
                    foreach (var row in slip.Rows)
                    {
                        var shade = index++ % 2 == 1;
                        IContainer Cell(IContainer cell) => (shade ? cell.Background(Colors.Grey.Lighten3) : cell).PaddingVertical(3).PaddingHorizontal(2);
                        table.Cell().Element(Cell).AlignRight().Text($"{Quantity(row.Quantity)}");
                        table.Cell().Element(Cell).AlignRight().Text(Pallets(row.Pallets));
                        table.Cell().Element(Cell).Text(row.Description ?? "");
                        table.Cell().Element(Cell).Text(row.Variety ?? "");
                        table.Cell().Element(Cell).Text(row.Packaging ?? "");
                        table.Cell().Element(Cell).Text(row.Brand ?? "");
                        table.Cell().Element(Cell).Text(row.Size ?? "");
                        table.Cell().Element(Cell).Text(row.Origin ?? "");
                        table.Cell().Element(Cell).AlignRight().Text(Kg(row.NetKg));
                        if (slip.ShowSupplier) table.Cell().Element(Cell).Text(row.Supplier ?? (row.PalletReference == null ? L("notReserved") : row.PalletReference)).FontSize(8);
                    }
                    // Het totaal alleen aan het eind (geen tabelvoet: die zou op elke pagina staan).
                    IContainer Total(IContainer cell) => cell.BorderTop(1.5f).PaddingVertical(4).PaddingHorizontal(2);
                    table.Cell().Element(Total).AlignRight().Text(Quantity(slip.TotalQuantity)).Bold();
                    table.Cell().Element(Total).AlignRight().Text(Pallets(slip.TotalPallets)).Bold();
                    table.Cell().ColumnSpan(6).Element(Total).Text($"{L("total")}: {Quantity(slip.TotalQuantity)} {L("packages")} · {Pallets(slip.TotalPallets)} {L("pallets")}").Bold();
                    table.Cell().Element(Total).AlignRight().Text(Kg(slip.TotalNetKg)).Bold();
                    if (slip.ShowSupplier) table.Cell().Element(Total);
                });

                if (slip.Packaging.Count > 0)
                {
                    column.Item().PaddingTop(10).Text(L("material")).Bold();
                    foreach (var item in slip.Packaging) column.Item().Text($"{item.Quantity} × {item.Description}");
                }

                column.Item().PaddingTop(18).ShowEntire().Column(sign =>
                {
                    sign.Item().Text($"{L("remarks")}:");
                    sign.Item().PaddingTop(14).BorderBottom(0.75f).Text("");
                    sign.Item().PaddingTop(16).Row(row =>
                    {
                        void Box(string label) => row.RelativeItem().PaddingRight(10).Column(box =>
                        {
                            box.Item().Height(34).Border(0.75f).Text("");
                            box.Item().PaddingTop(2).Text(label).FontSize(8);
                        });
                        Box(L("driver"));
                        Box(L("signature"));
                        Box(L("date"));
                    });
                });
            });

            private IEnumerable<string> Columns()
            {
                var keys = new List<string> { "quantity", "pallet", "description", "variety", "packaging", "brand", "size", "origin", "netKg" };
                if (slip.ShowSupplier) keys.Add("supplier");
                return keys;
            }
        }
    }
}
