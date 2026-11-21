using System.Text.Json.Nodes;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.DocumentAi
{
    // Certificaten deel 2: alles wat het voorstel van een certificaat nodig heeft uit de database (eigen producten, de certificaten van de
    // eigenaar, de naam van de relatie of het eigen bedrijf, de dag en de waarschuwingstermijn), voor het tonen én voor het goedkeuren.
    public sealed record CertificateProposalContext(List<CertificateProposals.OwnProduct> Products, List<CertificateProposals.StoredCertificate> Stored,
        List<string> OwnerNames, DateOnly Today, int WarningDays)
    {
        public static async Task<CertificateProposalContext> LoadAsync(AppDbContext db, int? relationId, CancellationToken cancellationToken)
        {
            var products = await db.Products.AsNoTracking().OrderBy(p => p.Name).Select(p => new CertificateProposals.OwnProduct(p.Id, p.Name)).ToListAsync(cancellationToken);
            var stored = await db.RelationCertificates.AsNoTracking().Where(c => c.RelationId == relationId).OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                .Select(c => new CertificateProposals.StoredCertificate(c.Id, c.TypeCode, c.CustomTypeName, c.Number)).ToListAsync(cancellationToken);
            var names = relationId == null
                ? await db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).Select(c => c.CompanyName).Take(1).ToListAsync(cancellationToken)
                : (await db.Relations.AsNoTracking().Where(r => r.Id == relationId).Select(r => new { r.Name, r.TradeName }).ToListAsync(cancellationToken))
                    .SelectMany(r => new[] { r.Name, r.TradeName }).ToList();
            return new CertificateProposalContext(products, stored, names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList(), db.BusinessCalendar.Today(),
                await CertificateService.WarningDaysAsync(db, cancellationToken));
        }

        public AiCertificateProposalDto Build(JsonObject json, out List<AiProblemDto> problems) =>
            CertificateProposals.Build(json, Products, Stored, OwnerNames, Today, WarningDays, out problems);
    }
}
