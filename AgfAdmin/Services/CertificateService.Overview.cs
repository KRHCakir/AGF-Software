using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Certificaten deel 1, de overzichten: alle certificaten (ook die bijna verlopen), de samenvatting per relatie voor de relatielijst en
    // de GGN's van een leverancier voor het voorstel bij ontvangst.
    public sealed partial class CertificateService
    {
        // Alle certificaten (Stamgegevens › Certificaten), of alleen die aandacht vragen (attention: bijna verlopen en verlopen, gesorteerd
        // op einddatum). Alleen actieve certificaten van actieve relaties en het eigen bedrijf, tenzij includeArchived.
        public async Task<CertificateOverviewDto> OverviewAsync(string query, string type, string status, bool includeArchived, CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(tracking: false, cancellationToken);
            var today = db.BusinessCalendar.Today();
            var warningUntil = today.AddDays(settings.CertificateWarningDays);
            var certificates = db.RelationCertificates.AsNoTracking().Where(c => c.RelationId == null || c.Relation.IsActive);
            if (!includeArchived) certificates = certificates.Where(c => c.IsActive);
            if (!string.IsNullOrWhiteSpace(type)) certificates = certificates.Where(c => c.TypeCode == type.Trim());
            certificates = status switch
            {
                "attention" => certificates.Where(c => c.ValidUntil != null && c.ValidUntil <= warningUntil),
                CertificateRules.StatusExpired => certificates.Where(c => c.ValidUntil != null && c.ValidUntil < today),
                CertificateRules.StatusExpiringSoon => certificates.Where(c => c.ValidUntil != null && c.ValidUntil >= today && c.ValidUntil <= warningUntil),
                CertificateRules.StatusValid => certificates.Where(c => c.ValidUntil != null && c.ValidUntil > warningUntil),
                CertificateRules.StatusNoEndDate => certificates.Where(c => c.ValidUntil == null),
                "productsPending" => certificates.Where(c => c.ProductsPending),
                _ => certificates,
            };
            var term = (query ?? "").Trim().ToLowerInvariant();
            var key = CertificateRules.NumberKey(term);
            if (term != "")
            {
                certificates = certificates.Where(c => (key != null && c.NumberKey.Contains(key)) || (c.Relation != null && c.Relation.Name.ToLower().Contains(term)));
            }
            var rows = await certificates
                .OrderBy(c => c.ValidUntil == null).ThenBy(c => c.ValidUntil).ThenBy(c => c.Relation == null ? "" : c.Relation.Name).ThenBy(c => c.Id)
                .Take(500)
                .Select(c => new
                {
                    c.Id, c.RelationId, RelationName = c.Relation == null ? null : c.Relation.Name,
                    IsCustomer = c.Relation != null && c.Relation.Customer != null, IsVendor = c.Relation != null && c.Relation.Vendor != null, IsCarrier = c.Relation != null && c.Relation.IsCarrier,
                    c.TypeCode, c.CustomTypeName, c.Number, c.ValidUntil, c.ProductsPending, c.IsActive,
                    Products = c.Products.OrderBy(p => p.Product.Name).Select(p => p.Product.Name).ToList(),
                })
                .ToListAsync(cancellationToken);
            return new CertificateOverviewDto
            {
                WarningDays = settings.CertificateWarningDays,
                Total = rows.Count,
                Items = rows.Select(c => new CertificateOverviewItemDto
                {
                    CertificateId = c.Id, RelationId = c.RelationId, RelationName = c.RelationName, IsCustomer = c.IsCustomer, IsVendor = c.IsVendor, IsCarrier = c.IsCarrier,
                    TypeCode = c.TypeCode, TypeName = CertificateRules.TypeName(c.TypeCode, c.CustomTypeName), Number = c.Number, ValidUntil = c.ValidUntil,
                    DaysLeft = c.ValidUntil is DateOnly until ? CertificateRules.DaysLeft(until, today) : null,
                    Status = CertificateRules.Status(c.ValidUntil, today, settings.CertificateWarningDays), ProductsPending = c.ProductsPending, IsActive = c.IsActive, ProductNames = c.Products,
                }).ToList(),
            };
        }

        // De samenvatting per relatie voor de relatielijst: per actief certificaat de soort en de status.
        public static async Task<Dictionary<int, List<RelationCertificateSummaryDto>>> SummariesAsync(AppDbContext db, IReadOnlyCollection<int> relationIds, CancellationToken cancellationToken)
        {
            if (relationIds.Count == 0) return new Dictionary<int, List<RelationCertificateSummaryDto>>();
            var warningDays = await WarningDaysAsync(db, cancellationToken);
            var today = db.BusinessCalendar.Today();
            var rows = await db.RelationCertificates.AsNoTracking().Where(c => c.IsActive && c.RelationId != null && relationIds.Contains(c.RelationId.Value))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                .Select(c => new { RelationId = c.RelationId.Value, c.TypeCode, c.CustomTypeName, c.ValidUntil, c.ProductsPending })
                .ToListAsync(cancellationToken);
            return rows.GroupBy(r => r.RelationId).ToDictionary(g => g.Key, g => g.Select(r => new RelationCertificateSummaryDto
            {
                TypeCode = r.TypeCode, TypeName = CertificateRules.TypeName(r.TypeCode, r.CustomTypeName), Status = CertificateRules.Status(r.ValidUntil, today, warningDays), ProductsPending = r.ProductsPending,
            }).ToList());
        }

        // De GGN's van leveranciers voor de ontvangst (voorstel per partij): de actieve GGN-certificaten van hun relatie, en een oud
        // leveranciers-GGN alleen zolang het (nog) niet als certificaat is overgenomen. Zo telt een gearchiveerd certificaat niet meer mee,
        // ook al staat het oude veld nog op actief.
        public static async Task<Dictionary<int, List<VendorGgnOptionDto>>> GgnOptionsAsync(AppDbContext db, IReadOnlyCollection<Vendor> vendors, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, List<VendorGgnOptionDto>>();
            if (vendors.Count == 0) return result;
            var vendorIds = vendors.Select(v => v.Id).Distinct().ToList();
            var relationIds = vendors.Select(v => v.RelationId).Distinct().ToList();
            var certificates = await db.RelationCertificates.AsNoTracking()
                .Where(c => c.RelationId != null && relationIds.Contains(c.RelationId.Value) && c.TypeCode == CertificateRules.GlobalGapIfa)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                .Select(c => new { RelationId = c.RelationId.Value, c.Number, c.ProducerName, c.CheckedOn, c.ValidUntil, c.HasGgnLabel, c.IsActive, c.SourceVendorGgnId })
                .ToListAsync(cancellationToken);
            var copied = certificates.Where(c => c.SourceVendorGgnId != null).Select(c => c.SourceVendorGgnId.Value).ToHashSet();
            var legacy = await db.VendorGgns.AsNoTracking().Where(g => vendorIds.Contains(g.VendorId) && g.IsActive).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToListAsync(cancellationToken);
            foreach (var vendor in vendors.DistinctBy(v => v.Id))
            {
                var options = certificates.Where(c => c.RelationId == vendor.RelationId && c.IsActive)
                    .Select(c => new VendorGgnOptionDto { Ggn = c.Number, ProducerName = c.ProducerName, CheckedOn = c.CheckedOn, CertificateValidUntil = c.ValidUntil, HasGgnLabel = c.HasGgnLabel })
                    .Concat(legacy.Where(g => g.VendorId == vendor.Id && !copied.Contains(g.Id))
                        .Select(g => new VendorGgnOptionDto { Ggn = g.Ggn, ProducerName = g.ProducerName, CheckedOn = g.CheckedOn, CertificateValidUntil = g.CertificateValidUntil, HasGgnLabel = g.HasGgnLabel }))
                    .DistinctBy(o => o.Ggn).ToList();
                result[vendor.Id] = options;
            }
            return result;
        }

    }
}
