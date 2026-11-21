using AgfAdmin.Data;
using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Het GGN van het product op een inkoopregel (certificaten deel 2). Op de factuur van een leverancier staat het GGN van de teler van het
    // product, bijna nooit dat van het bedrijf zelf. Het systeem stelt een nummer voor uit de geldige GGN-certificaten van de leverancier waarin
    // het product staat: één treffer wordt voorgevuld, meer treffers vragen een keuze, geen treffer blijft leeg. De gebruiker mag altijd
    // overschrijven of leegmaken: een handelaar factureert het GGN van zijn teler, en dat staat niet in zijn eigen certificaat.
    public static class ProductGgnRules
    {
        public const string ProposalNone = "None";
        public const string ProposalOne = "One";
        public const string ProposalMany = "Many";
        public const int MaxProposalProducts = 200;

        public sealed record GgnCertificate(int Id, string Number, DateOnly? ValidUntil, string ProducerName, IReadOnlyCollection<int> ProductIds);

        // Leeg = geen GGN (null); ingevuld altijd 13 cijfers (spaties als scheiding mogen). Een afwijkend controlecijfer is alleen een
        // waarschuwing (op het scherm, net als bij de certificaten).
        public static string Validate(string raw, string key, Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            var ggn = ArticleRules.NormalizeGgn(raw);
            if (ggn == null) return null;
            if (!ArticleRules.IsGgn(ggn))
            {
                addError(errors, key, $"A GGN consists of exactly {ArticleRules.GgnLength} digits (leading zeros included); spaces are allowed as separators.");
                return null;
            }
            return ggn;
        }

        // De voorstellen voor één product: de verschillende nummers van de GGN-certificaten waarin het product staat en die op de datum
        // geldig zijn (geen einddatum of niet eerder verlopen). Een gearchiveerd certificaat telt niet mee (dat doet de aanroeper).
        public static List<GgnCertificate> Matches(IEnumerable<GgnCertificate> certificates, int productId, DateOnly date) =>
            certificates.Where(c => c.ProductIds.Contains(productId) && (c.ValidUntil == null || c.ValidUntil >= date))
                .GroupBy(c => c.Number, StringComparer.Ordinal).Select(g => g.First()).ToList();

        public static string Kind(int matches) => matches switch { 0 => ProposalNone, 1 => ProposalOne, _ => ProposalMany };

        // Per product wat het systeem voorstelt bij deze leverancier op deze datum (verwachte levering, anders vandaag).
        public static async Task<ServiceResult<ProductGgnProposalsDto>> ProposalsAsync(AppDbContext db, int vendorId, IReadOnlyCollection<int> productIds, DateOnly date,
            CancellationToken cancellationToken)
        {
            var ids = productIds.Distinct().ToList();
            if (ids.Count > MaxProposalProducts)
            {
                return ServiceResult<ProductGgnProposalsDto>.Validation(new Dictionary<string, string[]> { ["productIds"] = new[] { $"At most {MaxProposalProducts} products." } });
            }
            var relationId = await db.Vendors.AsNoTracking().Where(v => v.Id == vendorId).Select(v => (int?)v.RelationId).SingleOrDefaultAsync(cancellationToken);
            if (relationId == null) return ServiceResult<ProductGgnProposalsDto>.NotFound($"Vendor {vendorId} does not exist.");
            var certificates = (await db.RelationCertificates.AsNoTracking()
                    .Where(c => c.RelationId == relationId && c.IsActive && c.TypeCode == CertificateRules.GlobalGapIfa)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                    .Select(c => new { c.Id, c.Number, c.ValidUntil, c.ProducerName, Products = c.Products.Select(p => p.ProductId).ToList() })
                    .ToListAsync(cancellationToken))
                .Select(c => new GgnCertificate(c.Id, c.Number, c.ValidUntil, c.ProducerName, c.Products)).ToList();
            return ServiceResult<ProductGgnProposalsDto>.Ok(new ProductGgnProposalsDto
            {
                VendorId = vendorId,
                Date = date,
                Proposals = ids.Select(productId =>
                {
                    var matches = Matches(certificates, productId, date);
                    return new ProductGgnProposalDto
                    {
                        ProductId = productId,
                        Kind = Kind(matches.Count),
                        Options = matches.Select(m => new ProductGgnOptionDto { Ggn = m.Number, CertificateId = m.Id, ValidUntil = m.ValidUntil, ProducerName = m.ProducerName }).ToList(),
                    };
                }).ToList(),
            });
        }
    }
}
