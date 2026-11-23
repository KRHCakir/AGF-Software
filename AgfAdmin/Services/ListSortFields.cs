using System;
using System.Collections.Generic;
using System.Linq;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // De toegestane sorteervelden van de lijsten die op de server bladeren (feedback 22 november, prompt 2). De sleutels zijn de namen die
    // de website stuurt (`sort`); alleen deze velden worden aangenomen. De waarde is wat de kolom toont.
    public static class ListSortFields
    {
        private static Dictionary<string, SortField<T>> Fields<T>(params (string Key, SortKind Kind, Func<T, object> Value)[] fields) =>
            fields.ToDictionary(f => f.Key, f => new SortField<T>(f.Kind, f.Value), StringComparer.Ordinal);

        private static string Joined(IEnumerable<string> values) => values == null ? null : string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

        // Status in de volgorde van het proces, niet op de Engelse code.
        private static int Rank(IReadOnlyList<string> order, string value)
        {
            var index = order.ToList().IndexOf(value);
            return index < 0 ? order.Count : index;
        }

        // Inkooporders: het hoofdnummer is het ordernummer van de leverancier (anders het eigen IO-nummer), zoals de lijst het toont.
        public static readonly IReadOnlyDictionary<string, SortField<PurchaseOrderListItemDto>> PurchaseOrders = Fields<PurchaseOrderListItemDto>(
            ("number", SortKind.Natural, i => string.IsNullOrWhiteSpace(i.SupplierReference) ? i.OrderNumber : i.SupplierReference.Trim()),
            ("orderNumber", SortKind.Natural, i => i.OrderNumber),
            ("vendor", SortKind.Text, i => i.VendorName),
            ("partij", SortKind.Natural, i => i.PartijNumber),
            ("orderedOn", SortKind.Date, i => i.OrderedOn),
            ("expected", SortKind.Date, i => i.ExpectedAt),
            ("status", SortKind.Number, i => Rank(PurchaseOrderRules.OrderStatuses, i.Status)),
            ("packages", SortKind.Number, i => i.ExpectedQuantity),
            ("netKg", SortKind.Number, i => i.ExpectedNetWeightKg),
            ("amount", SortKind.Number, i => i.AmountEur),
            ("invoices", SortKind.Natural, i => i.InvoiceNumbers.FirstOrDefault()?.InvoiceNumber));

        // Partijen (inkoop en verkoop in één API; de website biedt per soort de eigen kolommen aan).
        public static readonly IReadOnlyDictionary<string, SortField<PartijListItemDto>> Partijen = Fields<PartijListItemDto>(
            ("number", SortKind.Natural, i => i.Number),
            ("vendor", SortKind.Text, i => Joined(i.Vendors)),
            ("product", SortKind.Text, i => Joined(i.Products)),
            ("purchased", SortKind.Number, i => i.PurchasedQuantity),
            ("received", SortKind.Number, i => i.ReceivedQuantity),
            ("onHand", SortKind.Number, i => i.OnHandQuantity),
            ("sold", SortKind.Number, i => i.SoldQuantity),
            ("ordered", SortKind.Number, i => i.OrderedQuantity),
            ("status", SortKind.Number, i => Rank(new[] { PartijRules.StatusOpen, PartijRules.StatusSoldOut }, i.Status)),
            ("salesOrder", SortKind.Natural, i => i.SalesOrderNumber),
            ("customer", SortKind.Text, i => i.CustomerName),
            ("sources", SortKind.Natural, i => Joined(i.SourcePartijen)),
            ("createdAt", SortKind.Date, i => i.CreatedAt));

        // Relaties: rol als vaste groep (klant, leverancier, vervoerder en combinaties), status actief voor inactief.
        public static readonly IReadOnlyDictionary<string, SortField<RelationListItemDto>> Relations = Fields<RelationListItemDto>(
            ("code", SortKind.Natural, i => i.SearchCode),
            ("name", SortKind.Text, i => i.Name),
            ("role", SortKind.Number, i => (i.CustomerId != null ? 1 : 0) + (i.VendorId != null ? 2 : 0) + (i.IsCarrier ? 4 : 0)),
            ("status", SortKind.Number, i => i.IsActive ? 0 : 1),
            ("place", SortKind.Text, i => Joined(new[] { i.City, i.CountryCode })),
            ("contact", SortKind.Text, i => i.PrimaryContactName));

        // Certificaten: status van verlopen naar geldig (het meest dringend eerst), zonder einddatum achteraan.
        public static readonly IReadOnlyDictionary<string, SortField<CertificateOverviewItemDto>> Certificates = Fields<CertificateOverviewItemDto>(
            ("relation", SortKind.Text, i => i.RelationName),
            ("type", SortKind.Text, i => i.TypeName),
            ("number", SortKind.Natural, i => i.Number),
            ("products", SortKind.Text, i => Joined(i.ProductNames)),
            ("validUntil", SortKind.Date, i => i.ValidUntil),
            ("status", SortKind.Number, i => Rank(new[] { CertificateRules.StatusExpired, CertificateRules.StatusExpiringSoon, CertificateRules.StatusValid, CertificateRules.StatusNoEndDate }, i.Status)));
    }
}
