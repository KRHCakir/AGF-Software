using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // Genormaliseerde weergave van de oorspronkelijke invoeropdracht (POST api/purchase-invoices). Wordt bij het aanmaken op de
    // factuur bewaard (IncomingInvoice.RequestPayload) en bij een herhaling met dezelfde RequestId tekstueel vergeleken.
    // Zo blijft een exacte retry een retry, ook als leveranciers- of bedrijfsstandaarden (termijn, valuta, btw, ingeschakelde
    // optionele velden) intussen zijn gewijzigd, en geeft elke inhoudelijke afwijking, ook in optionele kopvelden, een conflict.
    // Normalisatie: tekst getrimd (leeg = null), valuta in hoofdletters, datums als kalenderdag, decimalen zonder slepende
    // nullen (10,0 = 10,00); alle velden, ook niet-ingeschakelde, tellen mee. De RequestId zelf hoort er niet bij.
    public static class PurchaseInvoiceCommand
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string Canonical(CreatePurchaseInvoiceDto request)
        {
            var lines = (request.Lines ?? new List<CreatePurchaseInvoiceLineDto>())
                .Select(l => l == null ? null : new LineSnapshot(l.ProductVariantId, Text(l.Description), Number(l.Quantity), Text(l.Unit), Number(l.UnitPrice), Number(l.DiscountPercentage), Number(l.VatPercentage)))
                .ToList();
            var snapshot = new Snapshot(
                request.VendorId, Text(request.InvoiceNumber), Day(request.InvoiceDate), request.PaymentTermsDays, Day(request.DueDate),
                Text(request.Currency)?.ToUpperInvariant(), lines, Text(request.InternalNote),
                Day(request.ReceivedDate), Text(request.PaymentReference), Text(request.Remarks), Number(request.GrossWeightKg), Number(request.NetWeightKg),
                Text(request.ContainerNumber), Text(request.Transporter), Text(request.DriverName), Text(request.LicensePlate), Text(request.Incoterms),
                Number(request.CustomsValue), Text(request.OriginCountry), Text(request.ReceiptReference));
            return JsonSerializer.Serialize(snapshot, Options);
        }

        public static bool Same(string stored, CreatePurchaseInvoiceDto request) =>
            stored != null && string.Equals(stored, Canonical(request), StringComparison.Ordinal);

        private static string Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Day(DateTime? value) => PurchaseInvoiceRules.AsUtcDate(value)?.ToString("yyyy-MM-dd");

        // Slepende nullen weg (1.50m en 1.5m zijn dezelfde waarde maar serialiseren anders).
        private static decimal? Number(decimal? value) => value == null ? null : value.Value / 1.000000000000000000000000000m;

        private sealed record Snapshot(
            int? VendorId, string InvoiceNumber, string InvoiceDate, int? PaymentTermsDays, string DueDate, string Currency, List<LineSnapshot> Lines, string InternalNote,
            string ReceivedDate, string PaymentReference, string Remarks, decimal? GrossWeightKg, decimal? NetWeightKg, string ContainerNumber,
            string Transporter, string DriverName, string LicensePlate, string Incoterms, decimal? CustomsValue, string OriginCountry, string ReceiptReference);

        private sealed record LineSnapshot(int? ProductVariantId, string Description, decimal? Quantity, string Unit, decimal? UnitPrice, decimal? DiscountPercentage, decimal? VatPercentage);
    }
}
