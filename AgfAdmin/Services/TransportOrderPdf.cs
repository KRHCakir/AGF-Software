using System.Globalization;
using AgfAdmin.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgfAdmin.Services
{
    // De transportopdracht als PDF (documenten deel 2), altijd uit de snapshot van één versie. Opbouw zoals het voorbeeld ("Transport
    // Order"): vervoerder, datum en onze referentie; "Please report with" met ordernummer, laadreferentie, laaddatum en -tijd, naast
    // temperatuur, losreferentie en losdatum; laad- en losplaats; de tabel (Quantity, Description, Class, Origin, Pallets, Pallet space(s),
    // Gross kg) met de kop op elke pagina en het totaal één keer aan het eind. Zelfde opmaak en getallen als de laadbon. Geen prijs, bedrag
    // of tarief, ook niet in de metadata of de bestandsnaam.
    public static class TransportOrderPdf
    {
        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        public static byte[] Render(SalesOrderDocument document, TransportOrderSnapshot order) => new Order(document, order).GeneratePdf();

        public static string FileName(SalesOrderDocument document) => $"Transportopdracht {document.Number} v{document.Version}.pdf";

        private static readonly Dictionary<string, Dictionary<string, string>> Labels = new()
        {
            [SalesOrderDocumentRules.LanguageEnglish] = new()
            {
                ["title"] = "Transport Order", ["transporter"] = "Transporter", ["own"] = "Own transport / customer pick-up", ["date"] = "Date",
                ["reference"] = "Our reference", ["reportWith"] = "Please report with", ["order"] = "Order number", ["loadingReference"] = "Loading reference",
                ["loadingDate"] = "Loading date", ["loadingTime"] = "Loading time", ["temperature"] = "Temperature", ["unloadingReference"] = "Unloading reference",
                ["unloadingDate"] = "Unloading date", ["loadingPlace"] = "Loading place", ["unloadingPlace"] = "Unloading place", ["quantity"] = "Quantity",
                ["description"] = "Description", ["class"] = "Class", ["origin"] = "Origin", ["pallets"] = "Pallets", ["spaces"] = "Pallet space(s)",
                ["grossKg"] = "Gross kg", ["total"] = "Total", ["packages"] = "packages", ["material"] = "Packaging material going along", ["remarks"] = "Remarks",
                ["report"] = "Please report when loading.", ["version"] = "version", ["page"] = "page", ["created"] = "made",
                ["kgPartial"] = "gross kg of the rows with a weight only",
            },
            [SalesOrderDocumentRules.LanguageDutch] = new()
            {
                ["title"] = "Transportopdracht", ["transporter"] = "Vervoerder", ["own"] = "Eigen transport / klant haalt op", ["date"] = "Datum",
                ["reference"] = "Onze referentie", ["reportWith"] = "Melden met", ["order"] = "Ordernummer", ["loadingReference"] = "Laadreferentie",
                ["loadingDate"] = "Laaddatum", ["loadingTime"] = "Laadtijd", ["temperature"] = "Temperatuur", ["unloadingReference"] = "Losreferentie",
                ["unloadingDate"] = "Losdatum", ["loadingPlace"] = "Laadplaats", ["unloadingPlace"] = "Losplaats", ["quantity"] = "Aantal",
                ["description"] = "Omschrijving", ["class"] = "Klasse", ["origin"] = "Herkomst", ["pallets"] = "Pallets", ["spaces"] = "Palletplaats(en)",
                ["grossKg"] = "Bruto kg", ["total"] = "Totaal", ["packages"] = "colli", ["material"] = "Emballage die mee gaat", ["remarks"] = "Opmerkingen",
                ["report"] = "Gelieve te melden bij het laden.", ["version"] = "versie", ["page"] = "pagina", ["created"] = "gemaakt",
                ["kgPartial"] = "bruto kg alleen van de regels met een gewicht",
            },
        };

        private static string Local(DateTime utc, string format) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), AmsterdamBusinessCalendar.Zone).ToString(format, Dutch);

        public static string Temperature(decimal? value) => value == null ? "–" : $"{value.Value.ToString("0.#", Dutch)} °C";

        private sealed class Order(SalesOrderDocument document, TransportOrderSnapshot order) : IDocument
        {
            private string L(string key) => (Labels.GetValueOrDefault(order.Language) ?? Labels[SalesOrderDocumentRules.LanguageEnglish])[key];

            public DocumentMetadata GetMetadata() => new() { Title = $"{L("title")} {document.Number} v{document.Version}", Author = order.CompanyName, Subject = order.OrderNumber };

            public void Compose(IDocumentContainer container) => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                // Zonder ligaturen, zoals de laadbon: zoeken en kopiëren in de PDF blijft werken.
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));
                page.Header().Element(Header);
                page.Content().PaddingTop(8).Element(Content);
                page.Footer().AlignRight().Text(x =>
                {
                    x.Span($"{L("title")} {document.Number} · {L("version")} {document.Version} · {L("created")} {Local(document.CreatedAt, "dd-MM-yyyy HH:mm")} · {L("page")} ").FontSize(7);
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
                        if (!string.IsNullOrWhiteSpace(order.LogoImage)) company.Item().Width(110).Image(Convert.FromBase64String(order.LogoImage));
                        company.Item().Text(order.CompanyName).Bold().FontSize(11);
                        if (order.CompanyAddress != null) company.Item().Text(order.CompanyAddress).FontSize(8);
                    });
                    row.ConstantItem(220).AlignRight().Column(title =>
                    {
                        title.Item().AlignRight().Text(L("title")).Bold().FontSize(20);
                        title.Item().AlignRight().Text($"{document.Number} · {L("version")} {document.Version}").FontSize(8);
                    });
                });
                column.Item().PaddingTop(6).BorderTop(1).PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(80); c.RelativeColumn(); c.ConstantColumn(80); c.RelativeColumn(); });
                    Field(table, L("transporter"), order.OwnTransport ? L("own") : string.Join("\n", new[] { order.CarrierName, order.CarrierAddress }.Where(p => p != null)), true);
                    Field(table, L("date"), Local(document.CreatedAt, "dd-MM-yyyy"));
                    Field(table, L("reference"), document.Number, true);
                    table.Cell(); table.Cell();
                });
            });

            private static void Field(TableDescriptor table, string label, string value, bool bold = false)
            {
                table.Cell().PaddingVertical(2).Text(label).FontSize(8).FontColor(Colors.Grey.Darken3);
                var text = table.Cell().PaddingVertical(2).Text(value ?? "");
                if (bold) text.Bold();
            }

            private void Content(IContainer container) => container.Column(column =>
            {
                // Melden met: links ordernummer, laadreferentie, laaddatum en -tijd; rechts temperatuur, losreferentie en losdatum.
                column.Item().Border(0.75f).Padding(6).Column(report =>
                {
                    report.Item().Text(L("reportWith")).Bold();
                    report.Item().PaddingTop(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.ConstantColumn(90); c.RelativeColumn(); c.ConstantColumn(90); c.RelativeColumn(); });
                        var loading = order.LoadAt ?? order.RequestedDeliveryDate;
                        Field(table, L("order"), order.OrderNumber, true);
                        Field(table, L("temperature"), Temperature(order.TemperatureC), order.TemperatureC != null);
                        Field(table, L("loadingReference"), order.LoadingReference ?? "–");
                        Field(table, L("unloadingReference"), order.UnloadingReference ?? "–");
                        Field(table, L("loadingDate"), Local(loading, "dd-MM-yyyy"), true);
                        Field(table, L("unloadingDate"), order.UnloadDate == null ? "–" : order.UnloadDate.Value.ToString("dd-MM-yyyy", Dutch), order.UnloadDate != null);
                        Field(table, L("loadingTime"), order.LoadAt == null ? "–" : Local(order.LoadAt.Value, "HH:mm"), order.LoadAt != null);
                        table.Cell(); table.Cell();
                    });
                });
                column.Item().PaddingTop(6).Row(row =>
                {
                    void Place(string label, string value) => row.RelativeItem().Border(0.75f).Padding(6).Column(place =>
                    {
                        place.Item().Text(label).Bold();
                        place.Item().PaddingTop(2).Text(value ?? "–");
                    });
                    Place(L("loadingPlace"), order.LoadingPlace);
                    row.ConstantItem(6);
                    Place(L("unloadingPlace"), order.UnloadingPlace);
                });

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(52); c.RelativeColumn(2f); c.ConstantColumn(36); c.ConstantColumn(40); c.RelativeColumn(1.7f); c.ConstantColumn(58); c.ConstantColumn(56);
                    });
                    table.Header(header =>
                    {
                        IContainer Head(IContainer cell) => cell.Background(Colors.Grey.Lighten2).BorderBottom(1).PaddingVertical(3).PaddingHorizontal(2);
                        foreach (var key in new[] { "quantity", "description", "class", "origin", "pallets", "spaces", "grossKg" }) header.Cell().Element(Head).Text(L(key)).Bold().FontSize(8);
                    });
                    var index = 0;
                    foreach (var row in order.Rows)
                    {
                        var shade = index++ % 2 == 1;
                        IContainer Cell(IContainer cell) => (shade ? cell.Background(Colors.Grey.Lighten3) : cell).PaddingVertical(3).PaddingHorizontal(2);
                        table.Cell().Element(Cell).AlignRight().Text(LoadingSlipPdf.Quantity(row.Quantity));
                        table.Cell().Element(Cell).Text(row.Description ?? "");
                        table.Cell().Element(Cell).Text(row.Class ?? "");
                        table.Cell().Element(Cell).Text(row.Origin ?? "");
                        table.Cell().Element(Cell).Text(order.PalletKind ?? "");
                        // Twee decimalen, zelfde berekening als de orderregel (niet naar hele pallets).
                        table.Cell().Element(Cell).AlignRight().Text(LoadingSlipPdf.Pallets(row.PalletSpaces));
                        table.Cell().Element(Cell).AlignRight().Text(LoadingSlipPdf.Kg(row.GrossKg));
                    }
                    IContainer Total(IContainer cell) => cell.BorderTop(1.5f).PaddingVertical(4).PaddingHorizontal(2);
                    var kgPartial = order.TotalGrossKg != null && order.Rows.Any(r => r.GrossKg == null);
                    table.Cell().Element(Total).AlignRight().Text(LoadingSlipPdf.Quantity(order.TotalQuantity)).Bold();
                    table.Cell().ColumnSpan(4).Element(Total).Text(text =>
                    {
                        text.Span($"{L("total")}: {LoadingSlipPdf.Quantity(order.TotalQuantity)} {L("packages")} · {LoadingSlipPdf.Pallets(order.TotalPalletSpaces)} {L("spaces").ToLowerInvariant()}").Bold();
                        if (kgPartial) text.Span($" · {L("kgPartial")}").FontSize(8);
                    });
                    table.Cell().Element(Total).AlignRight().Text(LoadingSlipPdf.Pallets(order.TotalPalletSpaces)).Bold();
                    table.Cell().Element(Total).AlignRight().Text(LoadingSlipPdf.Kg(order.TotalGrossKg)).Bold();
                });

                if (order.Packaging.Count > 0)
                {
                    column.Item().PaddingTop(10).Text(L("material")).Bold();
                    foreach (var item in order.Packaging) column.Item().Text($"{item.Quantity} × {item.Description}");
                }

                column.Item().PaddingTop(16).ShowEntire().Column(bottom =>
                {
                    bottom.Item().Text($"{L("remarks")}:").Bold();
                    if (order.Remarks != null) bottom.Item().PaddingTop(2).Text(order.Remarks);
                    bottom.Item().PaddingTop(14).BorderBottom(0.75f).Text("");
                    bottom.Item().PaddingTop(12).Text(L("report")).Bold();
                });
            });
        }
    }
}
