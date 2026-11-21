using System.Globalization;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // "Klopt dit?" bij inkoop (certificaten deel 1): heeft de leverancier een GGN-certificaat en staat het product van een regel er niet in,
    // of is het certificaat verlopen of verloopt het vóór de verwachte levering, dan vraagt de server een bevestiging (409 met code
    // CERTIFICATE_CONFIRMATION_REQUIRED en per product wat er aan de hand is). Een waarschuwing, geen blokkade: dezelfde opdracht met de
    // bevestigde producten slaat op, en legt per product vast wie bevestigde (CurrentUser), wanneer en wat er stond (regel 33).
    // Geen GGN, of alleen GGN's zonder producten ("producten nog invullen"): geen vraag over producten.
    public static class CertificateCheck
    {
        public const string Code = "CERTIFICATE_CONFIRMATION_REQUIRED";
        public const string KindNotCovered = "NotCovered";
        public const string KindExpired = "Expired";
        public const string KindExpiresBeforeDelivery = "ExpiresBeforeDelivery";
        public const string ContextPurchaseOrder = "PurchaseOrder";
        public const string ContextReceipt = "Receipt";

        private sealed record Ggn(int Id, string Number, DateOnly? ValidUntil, List<int> ProductIds, List<string> ProductNames);

        // De vragen voor deze producten bij deze leverancier, op de verwachte leverdatum (ontvangst: vandaag).
        public static async Task<List<CertificateCheckDto>> CheckAsync(AppDbContext db, int vendorId, IReadOnlyCollection<int> productIds, DateOnly deliveryDate, CancellationToken cancellationToken)
        {
            var result = new List<CertificateCheckDto>();
            if (productIds.Count == 0) return result;
            var vendor = await db.Vendors.AsNoTracking().Where(v => v.Id == vendorId).Select(v => new { v.RelationId, v.Name, RelationName = v.Relation.Name }).SingleOrDefaultAsync(cancellationToken);
            if (vendor == null) return result;
            var ggns = (await db.RelationCertificates.AsNoTracking()
                    .Where(c => c.RelationId == vendor.RelationId && c.IsActive && c.TypeCode == CertificateRules.GlobalGapIfa)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                    .Select(c => new { c.Id, c.Number, c.ValidUntil, Products = c.Products.Select(p => new { p.ProductId, p.Product.Name }).ToList() })
                    .ToListAsync(cancellationToken))
                .Select(c => new Ggn(c.Id, c.Number, c.ValidUntil, c.Products.Select(p => p.ProductId).ToList(), c.Products.Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()))
                .ToList();
            var withProducts = ggns.Where(g => g.ProductIds.Count > 0).ToList();
            if (withProducts.Count == 0) return result;
            var today = db.BusinessCalendar.Today();
            var names = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            var supplier = vendor.RelationName ?? vendor.Name;
            foreach (var productId in productIds.Distinct())
            {
                var product = names.GetValueOrDefault(productId) ?? $"#{productId}";
                var covering = withProducts.Where(g => g.ProductIds.Contains(productId)).ToList();
                var kinds = new List<string>();
                var parts = new List<string>();
                List<Ggn> shown;
                if (covering.Count == 0)
                {
                    shown = withProducts;
                    kinds.Add(KindNotCovered);
                    var covered = withProducts.SelectMany(g => g.ProductNames).Distinct(StringComparer.CurrentCultureIgnoreCase).Select(n => n.ToLowerInvariant()).ToList();
                    parts.Add($"GGN {string.Join(", ", withProducts.Select(g => g.Number))} van {supplier} geldt voor {Join(covered)}, niet voor {product.ToLowerInvariant()}.");
                }
                else
                {
                    shown = covering;
                }
                // Verlopen of verloopt vóór de levering: alleen als geen enkel (getoond) certificaat op die dag nog geldt.
                if (shown.All(g => g.ValidUntil != null && g.ValidUntil < today))
                {
                    kinds.Add(KindExpired);
                    var last = shown.Max(g => g.ValidUntil!.Value);
                    parts.Add($"Het GGN-certificaat is verlopen op {last.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}.");
                }
                else if (shown.All(g => g.ValidUntil != null && g.ValidUntil < deliveryDate))
                {
                    kinds.Add(KindExpiresBeforeDelivery);
                    var last = shown.Max(g => g.ValidUntil!.Value);
                    parts.Add($"Het GGN-certificaat verloopt op {last.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}, vóór de verwachte levering op {deliveryDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}.");
                }
                if (kinds.Count == 0) continue;
                result.Add(new CertificateCheckDto
                {
                    ProductId = productId, ProductName = product, SupplierName = supplier, Kinds = kinds, DeliveryDate = deliveryDate,
                    Certificates = shown.Select(g => new CertificateCheckGgnDto { CertificateId = g.Id, Number = g.Number, ValidUntil = g.ValidUntil, ProductNames = g.ProductNames }).ToList(),
                    Message = string.Join(" ", parts),
                });
            }
            return result;
        }

        private static string Join(IReadOnlyList<string> items) => items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => $"{string.Join(", ", items.Take(items.Count - 1))} en {items[^1]}",
        };

        // Nog niet bevestigd: de producten met een vraag die niet in de bevestiging staan.
        public static List<CertificateCheckDto> Unconfirmed(IReadOnlyCollection<CertificateCheckDto> checks, IReadOnlyCollection<int> confirmedProductIds) =>
            checks.Where(c => confirmedProductIds == null || !confirmedProductIds.Contains(c.ProductId)).ToList();

        public static object ConflictBody(IReadOnlyCollection<CertificateCheckDto> checks) => new
        {
            code = Code,
            message = "Some products are not in the supplier's GGN certificate, or the certificate has expired; confirm to continue.",
            checks,
        };

        public static string ConflictMessage(IReadOnlyCollection<CertificateCheckDto> checks) => string.Join(" ", checks.Select(c => c.Message)) + " Klopt dit?";

        // De bevestigingen bij het opslaan (zelfde save als de order of de ontvangst).
        public static IEnumerable<CertificateConfirmation> Confirmations(IReadOnlyCollection<CertificateCheckDto> checks, string context, int vendorId, ICurrentUser user, DateTime now,
            SupplyCommitment order = null, int? orderId = null, PalletContentLine contentLine = null) =>
            checks.Select(c => new CertificateConfirmation
            {
                Context = context,
                SupplyCommitment = order,
                SupplyCommitmentId = orderId ?? 0,
                PalletContentLine = contentLine,
                VendorId = vendorId,
                ProductId = c.ProductId,
                Kinds = string.Join(",", c.Kinds),
                CertificateIds = string.Join(",", c.Certificates.Select(g => g.CertificateId)),
                Message = c.Message.Length > 2000 ? c.Message[..2000] : c.Message,
                ConfirmedBy = user.Actor(CertificateRules.MaxActorLength),
                ConfirmedAt = now,
            });
    }
}
