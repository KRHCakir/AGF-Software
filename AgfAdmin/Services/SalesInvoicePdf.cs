using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1: de verkoopfactuur als PDF, altijd uit een snapshot (zoals de laadbon). Een definitieve factuur gebruikt de snapshot die
    // bij Definitief is bewaard (SalesInvoiceDocument), een concept een live snapshot met "CONCEPT" erop. A4, zwart-wit leesbaar.
    public sealed record SalesInvoicePdfLine(string Kind, decimal Quantity, string Unit, string Description, string Origin, string Lots, string Ggn,
        decimal? NetKg, decimal? UnitPrice, decimal Discount, decimal VatPercentage, decimal? Amount);

    public sealed record SalesInvoicePdfVat(decimal Percentage, decimal Net, decimal Vat);

    public sealed record SalesInvoiceSnapshot(
        string Language, bool Concept,
        string CompanyName, string CompanyAddress, string CompanyEmail, string CompanyPhone, string CompanyKvk, string CompanyVat, string CompanyIban, string LogoImage,
        string CustomerName, string CustomerAddress, string CustomerVatNumber,
        string InvoiceNumber, DateTime InvoiceDate, DateTime? DeliveryDate, DateTime? DueDate, int? PaymentTermDays, string CustomerReference,
        IReadOnlyList<string> SalesLotNumbers, IReadOnlyList<string> OrderNumbers,
        IReadOnlyList<SalesInvoicePdfLine> Lines, IReadOnlyList<SalesInvoicePdfVat> VatRates, decimal? Net, decimal? Vat, decimal? Gross);

    public static class SalesInvoiceSnapshotBuilder
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        // Uit het antwoord van de service (concept: actuele klant en regels; definitief: de momentopname) en de bedrijfsgegevens van nu.
        public static SalesInvoiceSnapshot Build(SalesInvoiceDto invoice, CompanySettings settings, string language)
        {
            static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            var salesLots = invoice.Deliveries.Select(d => d.PartijNumber).Append(invoice.SalesLotNumber).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            return new SalesInvoiceSnapshot(
                language == SalesOrderDocumentRules.LanguageEnglish ? SalesOrderDocumentRules.LanguageEnglish : SalesOrderDocumentRules.LanguageDutch,
                invoice.Status != SalesInvoiceRules.StatusFinal,
                settings?.CompanyName ?? "", Blank(settings?.Address), Blank(settings?.Email), Blank(settings?.Phone), Blank(settings?.KvkNumber), Blank(settings?.VatNumber),
                Blank(settings?.Iban), LoadingSlipBuilder.LogoImageOf(settings?.LogoPath),
                invoice.CustomerName, Blank(invoice.CustomerAddress), Blank(invoice.CustomerVatNumber),
                invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DeliveryDate, invoice.DueDate, invoice.PaymentTermDays, Blank(invoice.ReferenceNumber),
                salesLots, invoice.Deliveries.Select(d => d.OrderNumber).Distinct().ToList(),
                invoice.Lines.Select(l => new SalesInvoicePdfLine(l.LineKind, l.Quantity, l.Unit, l.Description, Blank(l.Origin), Blank(l.LotNumber), Blank(l.Ggn),
                    l.NetWeightKg, l.UnitPrice, l.DiscountPercentage, l.VatPercentage, l.Amount)).ToList(),
                invoice.Totals.VatRates.Select(r => new SalesInvoicePdfVat(r.Percentage, r.Net, r.Vat)).ToList(),
                invoice.Totals.Net, invoice.Totals.Vat, invoice.Totals.Gross);
        }

        public static string Serialize(SalesInvoiceSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);
        public static SalesInvoiceSnapshot Deserialize(string json) => JsonSerializer.Deserialize<SalesInvoiceSnapshot>(json, Json);
        public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static class SalesInvoicePdf
    {
        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        public static byte[] Render(SalesInvoiceSnapshot invoice) => new Pdf(invoice).GeneratePdf();

        public static string FileName(SalesInvoiceSnapshot invoice) => invoice.Concept || invoice.InvoiceNumber == null
            ? "Factuur concept.pdf" : $"Factuur {invoice.InvoiceNumber}.pdf";

        private static readonly Dictionary<string, Dictionary<string, string>> Labels = new()
        {
            [SalesOrderDocumentRules.LanguageDutch] = new()
            {
                ["title"] = "Factuur", ["concept"] = "CONCEPT – geen geldige factuur", ["number"] = "Factuurnummer", ["date"] = "Factuurdatum", ["delivery"] = "Leverdatum",
                ["due"] = "Vervaldatum", ["reference"] = "Uw referentie", ["lots"] = "Partij (V)", ["orders"] = "Order", ["to"] = "Factuur aan", ["vatNumber"] = "Btw-nummer",
                ["kvk"] = "KvK", ["vat"] = "Btw", ["iban"] = "IBAN", ["quantity"] = "Colli", ["description"] = "Omschrijving", ["origin"] = "Herkomst", ["lot"] = "Partij / GGN",
                ["netKg"] = "Netto kg", ["price"] = "Prijs", ["per"] = "Per", ["discount"] = "Korting", ["vatPct"] = "Btw %", ["amount"] = "Bedrag",
                ["extras"] = "Emballage en diensten", ["subtotal"] = "Totaal excl. btw", ["vatOver"] = "Btw {0} over {1}", ["total"] = "Totaal incl. btw",
                ["pay"] = "Graag het totaal van {0} vóór {1} overmaken op {2} t.n.v. {3}, onder vermelding van factuurnummer {4}.",
                ["payNoIban"] = "Graag het totaal van {0} vóór {1} betalen, onder vermelding van factuurnummer {2}.", ["page"] = "pagina", ["noPrice"] = "zonder prijs",
            },
            [SalesOrderDocumentRules.LanguageEnglish] = new()
            {
                ["title"] = "Invoice", ["concept"] = "DRAFT – not a valid invoice", ["number"] = "Invoice number", ["date"] = "Invoice date", ["delivery"] = "Delivery date",
                ["due"] = "Due date", ["reference"] = "Your reference", ["lots"] = "Lot (V)", ["orders"] = "Order", ["to"] = "Invoice to", ["vatNumber"] = "VAT number",
                ["kvk"] = "CoC", ["vat"] = "VAT", ["iban"] = "IBAN", ["quantity"] = "Packages", ["description"] = "Description", ["origin"] = "Origin", ["lot"] = "Lot / GGN",
                ["netKg"] = "Net kg", ["price"] = "Price", ["per"] = "Per", ["discount"] = "Discount", ["vatPct"] = "VAT %", ["amount"] = "Amount",
                ["extras"] = "Packaging and services", ["subtotal"] = "Total excl. VAT", ["vatOver"] = "VAT {0} on {1}", ["total"] = "Total incl. VAT",
                ["pay"] = "Please pay the total of {0} before {1} to {2} in the name of {3}, quoting invoice number {4}.",
                ["payNoIban"] = "Please pay the total of {0} before {1}, quoting invoice number {2}.", ["page"] = "page", ["noPrice"] = "no price",
            },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Units = new()
        {
            [SalesOrderDocumentRules.LanguageDutch] = new() { ["box"] = "doos", ["colli"] = "colli", ["piece"] = "stuk", ["tray"] = "tray", ["dozen"] = "dozijn" },
            [SalesOrderDocumentRules.LanguageEnglish] = new() { ["box"] = "box", ["colli"] = "package", ["piece"] = "piece", ["tray"] = "tray", ["dozen"] = "dozen" },
        };

        public static string Money(decimal? value) => value == null ? "–" : "€ " + value.Value.ToString("N2", Dutch);
        public static string Quantity(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "#,##0.###", Dutch);
        private static string Percent(decimal value) => value.ToString(value == decimal.Truncate(value) ? "0" : "0.##", Dutch) + "%";
        private static string Date(DateTime? utc) => utc == null ? "–"
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), AmsterdamBusinessCalendar.Zone).ToString("dd-MM-yyyy", Dutch);

        private sealed class Pdf(SalesInvoiceSnapshot invoice) : IDocument
        {
            private string L(string key) => Labels[invoice.Language][key];
            private string Unit(string unit) => unit == null ? "" : Units[invoice.Language].GetValueOrDefault(unit, unit);

            public DocumentMetadata GetMetadata() => new() { Title = invoice.Concept ? $"{L("title")} (concept)" : $"{L("title")} {invoice.InvoiceNumber}", Author = invoice.CompanyName };

            public void Compose(IDocumentContainer container) => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));
                page.Header().Element(Header);
                page.Content().PaddingTop(10).Element(Content);
                page.Footer().Column(footer =>
                {
                    footer.Item().BorderTop(1).PaddingTop(4).Text(PayText()).FontSize(8);
                    footer.Item().AlignRight().Text(x =>
                    {
                        x.Span($"{(invoice.Concept ? L("concept") : invoice.InvoiceNumber)} · {L("page")} ").FontSize(7);
                        x.CurrentPageNumber().FontSize(7);
                        x.Span(" / ").FontSize(7);
                        x.TotalPages().FontSize(7);
                    });
                });
            });

            private string PayText()
            {
                var number = invoice.InvoiceNumber ?? "–";
                return invoice.CompanyIban != null
                    ? string.Format(Dutch, L("pay"), Money(invoice.Gross), Date(invoice.DueDate), invoice.CompanyIban, invoice.CompanyName, number)
                    : string.Format(Dutch, L("payNoIban"), Money(invoice.Gross), Date(invoice.DueDate), number);
            }

            private void Header(IContainer container) => container.Column(column =>
            {
                if (invoice.Concept)
                {
                    column.Item().Border(2).Padding(4).AlignCenter().Text(L("concept")).Bold().FontSize(14);
                }
                column.Item().PaddingTop(invoice.Concept ? 6 : 0).Row(row =>
                {
                    row.RelativeItem().Column(company =>
                    {
                        if (!string.IsNullOrWhiteSpace(invoice.LogoImage)) company.Item().Width(110).Image(Convert.FromBase64String(invoice.LogoImage));
                        company.Item().Text(invoice.CompanyName).Bold().FontSize(11);
                        if (invoice.CompanyAddress != null) company.Item().Text(invoice.CompanyAddress).FontSize(8);
                        if (invoice.CompanyEmail != null || invoice.CompanyPhone != null)
                            company.Item().Text(string.Join(" · ", new[] { invoice.CompanyEmail, invoice.CompanyPhone }.Where(v => v != null))).FontSize(8);
                        if (invoice.CompanyKvk != null) company.Item().Text($"{L("kvk")} {invoice.CompanyKvk}").FontSize(8);
                        if (invoice.CompanyVat != null) company.Item().Text($"{L("vat")} {invoice.CompanyVat}").FontSize(8);
                        if (invoice.CompanyIban != null) company.Item().Text($"{L("iban")} {invoice.CompanyIban}").FontSize(8);
                    });
                    row.ConstantItem(230).AlignRight().Column(title =>
                    {
                        title.Item().AlignRight().Text(L("title")).Bold().FontSize(20);
                        title.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            void Field(string label, string value, bool bold = false)
                            {
                                table.Cell().PaddingVertical(1).Text(label).FontSize(8);
                                var text = table.Cell().PaddingVertical(1).AlignRight().Text(value ?? "–");
                                if (bold) text.Bold();
                            }
                            Field(L("number"), invoice.InvoiceNumber ?? "–", true);
                            Field(L("date"), invoice.Concept ? "–" : Date(invoice.InvoiceDate));
                            Field(L("delivery"), Date(invoice.DeliveryDate));
                            Field(L("due"), Date(invoice.DueDate));
                            if (invoice.CustomerReference != null) Field(L("reference"), invoice.CustomerReference);
                            if (invoice.SalesLotNumbers.Count > 0) Field(L("lots"), string.Join(", ", invoice.SalesLotNumbers));
                            if (invoice.OrderNumbers.Count > 0) Field(L("orders"), string.Join(", ", invoice.OrderNumbers));
                        });
                    });
                });
                column.Item().PaddingTop(10).Border(1).Padding(6).Column(customer =>
                {
                    customer.Item().Text(L("to")).FontSize(8);
                    customer.Item().Text(invoice.CustomerName ?? "").Bold().FontSize(11);
                    if (invoice.CustomerAddress != null) customer.Item().Text(invoice.CustomerAddress);
                    if (invoice.CustomerVatNumber != null) customer.Item().Text($"{L("vatNumber")} {invoice.CustomerVatNumber}").FontSize(8);
                });
            });

            private void Content(IContainer container) => container.Column(column =>
            {
                var goods = invoice.Lines.Where(l => l.Kind == SalesInvoiceRules.LineProduct).ToList();
                var extras = invoice.Lines.Where(l => l.Kind != SalesInvoiceRules.LineProduct).ToList();
                if (goods.Count > 0) column.Item().Element(c => LineTable(c, goods, withGoods: true));
                if (extras.Count > 0)
                {
                    column.Item().PaddingTop(10).Text(L("extras")).Bold();
                    column.Item().Element(c => LineTable(c, extras, withGoods: false));
                }
                column.Item().PaddingTop(10).AlignRight().Width(260).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(90); });
                    table.Cell().Text(L("subtotal"));
                    table.Cell().AlignRight().Text(Money(invoice.Net));
                    // Btw per tarief apart (wettelijk verplicht): het tarief, de grondslag en het bedrag.
                    foreach (var rate in invoice.VatRates)
                    {
                        table.Cell().Text(string.Format(Dutch, L("vatOver"), Percent(rate.Percentage), Money(rate.Net)));
                        table.Cell().AlignRight().Text(Money(rate.Vat));
                    }
                    table.Cell().BorderTop(1).PaddingTop(2).Text(L("total")).Bold();
                    table.Cell().BorderTop(1).PaddingTop(2).AlignRight().Text(Money(invoice.Gross)).Bold();
                });
            });

            private void LineTable(IContainer container, List<SalesInvoicePdfLine> lines, bool withGoods) => container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(38);
                    c.RelativeColumn(3);
                    if (withGoods) { c.ConstantColumn(38); c.RelativeColumn(2); c.ConstantColumn(44); }
                    c.ConstantColumn(56);
                    c.ConstantColumn(34);
                    c.ConstantColumn(40);
                    c.ConstantColumn(34);
                    c.ConstantColumn(62);
                });
                table.Header(header =>
                {
                    void Head(string text, bool right = false)
                    {
                        var cell = header.Cell().BorderBottom(1).PaddingBottom(2);
                        (right ? cell.AlignRight() : cell).Text(text).Bold().FontSize(8);
                    }
                    Head(L("quantity"), true);
                    Head(L("description"));
                    if (withGoods) { Head(L("origin")); Head(L("lot")); Head(L("netKg"), true); }
                    Head(L("price"), true);
                    Head(L("per"));
                    Head(L("discount"), true);
                    Head(L("vatPct"), true);
                    Head(L("amount"), true);
                });
                var index = 0;
                foreach (var line in lines)
                {
                    var background = index++ % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
                    IContainer Cell() => table.Cell().Background(background).PaddingVertical(2).PaddingHorizontal(1);
                    Cell().AlignRight().Text(Quantity(line.Quantity));
                    Cell().Text(line.Description ?? "");
                    if (withGoods)
                    {
                        Cell().Text(line.Origin ?? "");
                        Cell().Column(lot =>
                        {
                            lot.Item().Text(line.Lots ?? "");
                            if (line.Ggn != null) lot.Item().Text($"GGN {line.Ggn}").FontSize(7);
                        });
                        Cell().AlignRight().Text(line.NetKg == null ? "" : WeightDisplay.Kg(line.NetKg, 0));
                    }
                    Cell().AlignRight().Text(line.UnitPrice == null ? L("noPrice") : Money(line.UnitPrice));
                    Cell().Text(Unit(line.Unit));
                    Cell().AlignRight().Text(line.Discount == 0m ? "" : Percent(line.Discount));
                    Cell().AlignRight().Text(Percent(line.VatPercentage));
                    Cell().AlignRight().Text(Money(line.Amount));
                }
            });
        }
    }
}
