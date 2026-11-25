using System.Globalization;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1: vaste waarden en kleine regels van het nieuwe factuurmodel (Invoice met Kind). Oude facturen (Kind leeg) vallen
    // hier buiten en blijven zoals ze zijn.
    public static class SalesInvoiceRules
    {
        public const string KindDelivery = "Delivery";
        public const string KindDirect = "Direct";
        public static readonly IReadOnlyList<string> Kinds = new[] { KindDelivery, KindDirect };

        public const string StatusConcept = "Concept";
        public const string StatusFinal = "Final";
        public const string StatusDiscarded = "Discarded";
        public static readonly IReadOnlyList<string> Statuses = new[] { StatusConcept, StatusFinal, StatusDiscarded };

        public const string LineProduct = "Product";
        public const string LineService = "Service";
        public const string LinePackaging = "Packaging";
        public static readonly IReadOnlyList<string> LineKinds = new[] { LineProduct, LineService, LinePackaging };

        public const string NumberPrefix = "VF";
        public const int SequenceDigits = 5;
        public const int MaxNumberLength = 20;
        public const int MaxActorLength = 100;
        public const int MaxDescriptionLength = 500;
        public const int MaxReasonLength = 500;
        public const int MaxReferenceLength = 100;
        public const int MaxAddressLength = 1000;
        public const int MaxLotNumberLength = 500;
        public const int MaxGgnLength = 500;
        public const int MaxRevenueTypeNameLength = 100;
        public const int MaxExternalCodeLength = 50;
        // Een concept dat langer dan dit ligt, krijgt een waarschuwing in de lijst.
        public const int StaleConceptDays = 7;
        // Betalingstermijn als de klant er geen heeft.
        public const int DefaultPaymentTermDays = 14;
        public const string DefaultRevenueTypeName = "Omzet diensten";

        // VF-2026-00001: doorlopend per jaar, zonder gaten.
        public static string FormatNumber(int year, int sequence) =>
            string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year:D4}-{sequence.ToString($"D{SequenceDigits}", CultureInfo.InvariantCulture)}");

        public static bool IsNewModel(Invoice invoice) => invoice.Kind != null;

        // Een regel telt mee (in totalen, PDF en "al gefactureerd") zolang ze niet uit het concept is gehaald.
        public static bool Counts(InvoiceLine line) => line.RemovedAt == null;

        // Bedrag en btw van een regel, op centen (dezelfde rekenregels als de order: OrderLineMath en InvoiceMath).
        public static void Recalculate(InvoiceLine line)
        {
            line.LineNet = line.UnitPrice == null ? null : InvoiceMath.LineNet(line.Quantity, line.UnitPrice.Value, line.DiscountPercentage);
            line.LineVat = line.LineNet == null ? null : InvoiceMath.LineVat(line.LineNet.Value, line.VATPercentage);
        }

        // Kop-totalen uit de meetellende regels met een bedrag; btw per regel op centen, daarna per tarief opgeteld.
        public static void RecalculateTotals(Invoice invoice)
        {
            foreach (var line in invoice.InvoiceLines.Where(Counts))
            {
                Recalculate(line);
            }
            var summary = Summary(invoice.InvoiceLines);
            invoice.SubTotal = summary.Net ?? 0m;
            invoice.VATAmount = summary.Vat ?? 0m;
            invoice.TotalAmount = summary.Gross ?? 0m;
        }

        public static OrderLineMath.Summary Summary(IEnumerable<InvoiceLine> lines) =>
            OrderLineMath.Summarize(lines.Where(Counts).Select(l => new OrderLineMath.Line(l.LineNet, l.VATPercentage)));

        // Wat Definitief nodig heeft; leeg = klaar. Sleutels in de vorm van het formulier.
        public static Dictionary<string, string[]> FinalizeErrors(Invoice invoice)
        {
            var errors = new Dictionary<string, string[]>();
            var lines = invoice.InvoiceLines.Where(Counts).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
            if (lines.Count == 0)
            {
                errors["lines"] = new[] { "An invoice needs at least one line." };
            }
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].UnitPrice == null)
                {
                    errors[$"lines[{i}].unitPrice"] = new[] { "Every line needs a price before the invoice is final." };
                }
                if (!ArticleRules.IsVatRate(lines[i].VATPercentage))
                {
                    errors[$"lines[{i}].vatPercentage"] = new[] { $"VAT must be one of: {string.Join(", ", ArticleRules.VatRates)} percent." };
                }
            }
            if (invoice.CustomerId == null)
            {
                errors["customerId"] = new[] { "An invoice needs a customer." };
            }
            return errors;
        }

        // Vervaldatum: factuurdatum (Europe/Amsterdam) plus de betalingstermijn in dagen.
        public static DateTime DueDate(DateTime invoiceDateUtc, int paymentTermDays) =>
            DateTime.SpecifyKind(AmsterdamBusinessCalendar.ToAmsterdamDate(invoiceDateUtc).AddDays(paymentTermDays).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }
}
