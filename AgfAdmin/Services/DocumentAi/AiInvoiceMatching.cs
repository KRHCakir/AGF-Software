using System.Text.RegularExpressions;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.DocumentAi
{
    // Koppelen aan wat er al is, alleen als voorstel (de gebruiker bevestigt; er wordt nooit een leverancier of artikel aangemaakt):
    // - leverancier: actieve relaties met een leveranciersrol, op IBAN, KvK-nummer, btw-nummer en naam; voorgesteld bij precies één
    //   treffer (of precies één op een nummer), anders een keuze;
    // - inkooporder: op het ordernummer van de leverancier (stap 3, dezelfde sleutel), bij die leverancier als die bekend is; voorgesteld bij
    //   precies één niet-geannuleerde order;
    // - dubbele factuur: dezelfde leverancier met hetzelfde factuurnummer (dezelfde sleutel als bij opslaan);
    // - kostenartikel per regel: een Dienst-artikel (Handling, Transport, QC, Zeevracht, Afval) op trefwoorden in de omschrijving.
    public sealed partial class AiInvoiceMatching(AppDbContext db)
    {
        private static readonly (string Name, string[] Words)[] CostWords =
        [
            ("Zeevracht", ["zeevracht", "sea freight", "ocean freight", "seefracht", "zeevervoer"]),
            ("Transport", ["transport", "vracht", "freight", "fracht", "trucking", "vervoer", "shipping", "carriage", "bezorg"]),
            ("Handling", ["handling", "laden en lossen", "loading", "umschlag", "overslag"]),
            ("QC", ["qc", "kwaliteitscontrole", "keuring", "inspectie", "inspection", "quality control"]),
            ("Afval", ["afval", "waste", "disposal", "entsorgung", "verwerking afval"]),
        ];

        [GeneratedRegex(@"\b(b\.?\s?v\.?|n\.?\s?v\.?|v\.?\s?o\.?\s?f\.?|gmbh|ltd|limited|s\.?\s?l\.?|s\.?\s?a\.?|s\.?\s?r\.?\s?l\.?|inc|co)\b", RegexOptions.IgnoreCase)]
        private static partial Regex LegalForm();

        [GeneratedRegex(@"[^a-z0-9]")]
        private static partial Regex NonAlphanumeric();

        public static string NameKey(string name) => NonAlphanumeric().Replace(LegalForm().Replace((name ?? "").ToLowerInvariant(), " "), "");
        public static string IdKey(string value) => NonAlphanumeric().Replace((value ?? "").ToLowerInvariant(), "");

        public async Task<AiMatchesDto> MatchAsync(AiInvoiceProposalDto proposal, CancellationToken cancellationToken = default)
        {
            var matches = new AiMatchesDto();
            if (proposal == null) return matches;

            var vendors = await db.Vendors.AsNoTracking()
                .Where(v => v.Relation == null || v.Relation.IsActive)
                .Select(v => new { v.Id, v.Name, Vat = v.Relation.VatNumber, Registration = v.Relation.RegistrationNumber, RelationId = (int?)v.RelationId })
                .ToListAsync(cancellationToken);
            var ibans = await db.RelationBankAccounts.AsNoTracking().Where(a => a.IsActive && a.Iban != null)
                .Select(a => new { a.RelationId, a.Iban }).ToListAsync(cancellationToken);
            var iban = IdKey(proposal.VendorIban);
            var vat = IdKey(proposal.VendorVatNumber);
            var registration = IdKey(proposal.VendorRegistrationNumber);
            var name = NameKey(proposal.VendorName);
            foreach (var vendor in vendors)
            {
                var on = new List<string>();
                if (iban.Length >= 8 && ibans.Any(a => a.RelationId == vendor.RelationId && IdKey(a.Iban) == iban)) on.Add("iban");
                if (registration.Length >= 6 && IdKey(vendor.Registration) == registration) on.Add("kvk");
                if (vat.Length >= 8 && IdKey(vendor.Vat) == vat) on.Add("vat");
                var vendorName = NameKey(vendor.Name);
                if (name.Length >= 3 && vendorName.Length >= 3 && (vendorName == name || (Math.Min(name.Length, vendorName.Length) >= 5 && (vendorName.Contains(name) || name.Contains(vendorName))))) on.Add("name");
                if (on.Count > 0) matches.VendorCandidates.Add(new AiVendorCandidateDto { VendorId = vendor.Id, Name = vendor.Name, MatchedOn = on });
            }
            matches.VendorCandidates = matches.VendorCandidates.OrderByDescending(c => c.MatchedOn.Count(m => m != "name")).ThenByDescending(c => c.MatchedOn.Count).ThenBy(c => c.Name).Take(10).ToList();
            var byNumber = matches.VendorCandidates.Where(c => c.MatchedOn.Any(m => m != "name")).ToList();
            matches.SuggestedVendorId = byNumber.Count == 1 ? byNumber[0].VendorId : byNumber.Count == 0 && matches.VendorCandidates.Count == 1 ? matches.VendorCandidates[0].VendorId : null;

            var orderKey = PurchaseOrderRules.SupplierReferenceKey(proposal.SupplierOrderNumber);
            if (orderKey != null)
            {
                var orders = db.SupplyCommitments.AsNoTracking().Where(c => c.Status != SupplyRules.StatusCancelled && c.SupplierReference != null
                    && c.SupplierReference.ToUpper().Replace(" ", "").Replace("\t", "").Replace(" ", "").Replace("-", "") == orderKey);
                if (matches.SuggestedVendorId is int vendorId) orders = orders.Where(c => c.VendorId == vendorId);
                matches.OrderCandidates = await orders.OrderByDescending(c => c.OrderYear).ThenByDescending(c => c.OrderSequence).Take(5)
                    .Select(c => new AiOrderCandidateDto { Id = c.Id, OrderNumber = c.OrderNumber, SupplierReference = c.SupplierReference, VendorId = c.VendorId, VendorName = c.Vendor.Name })
                    .ToListAsync(cancellationToken);
                matches.SuggestedOrderId = matches.OrderCandidates.Count == 1 ? matches.OrderCandidates[0].Id : null;
            }

            var numberKey = PurchaseOrderRules.InvoiceNumberKey(proposal.InvoiceNumber);
            if (matches.SuggestedVendorId is int suggested && numberKey != null)
            {
                matches.DuplicateInvoice = await db.IncomingInvoices.AsNoTracking()
                    .Where(i => i.VendorId == suggested && i.InvoiceNumber != null && i.InvoiceNumber.Trim().ToLower() == numberKey)
                    .Select(i => new AiDuplicateInvoiceDto { Id = i.Id, InvoiceNumber = i.InvoiceNumber }).FirstOrDefaultAsync(cancellationToken);
            }

            var costs = await db.CostArticles.AsNoTracking().Where(c => c.IsActive && c.Kind == CostArticleRules.KindCost).Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
            for (var i = 0; i < proposal.Lines.Count; i++)
            {
                var description = (proposal.Lines[i].Description ?? "").ToLowerInvariant();
                var hit = CostWords.FirstOrDefault(c => c.Words.Any(w => Regex.IsMatch(description, $@"(^|[^a-z]){Regex.Escape(w)}")));
                var cost = hit.Name == null ? null : costs.FirstOrDefault(c => string.Equals(c.Name, hit.Name, StringComparison.OrdinalIgnoreCase));
                if (cost != null) matches.LineCostArticles.Add(new AiLineCostArticleDto { Index = i, CostArticleId = cost.Id, Name = cost.Name });
            }
            return matches;
        }
    }
}
