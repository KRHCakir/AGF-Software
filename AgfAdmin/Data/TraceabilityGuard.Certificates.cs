using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 33: certificaten (certificaten deel 1, zie CertificateService) ----
        //
        // 1. een certificaat wordt nooit verwijderd (archiveren = inactief): er hangen bestanden en bevestigingen aan;
        // 2. bij wie een opgeslagen certificaat hoort (relatie of eigen bedrijf) en uit welk oud GGN het is overgenomen, ligt vast;
        // 3. een bestand van een certificaat wordt nooit gewijzigd of verwijderd, en een nieuw bestand is de volgende versie van dat
        //    certificaat (vervangen = een nieuwe versie, nooit stil wissen);
        // 4. een bevestiging "Klopt dit?" bij inkoop wordt nooit gewijzigd of verwijderd.
        // Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        private static async Task CheckCertificatesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<RelationCertificate>().Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A certificate is never removed; archive it instead.");
            }
            if (db.ChangeTracker.Entries<RelationCertificateFile>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A certificate file is never changed or removed; upload a new version instead.");
            }
            if (db.ChangeTracker.Entries<CertificateConfirmation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A certificate confirmation is never changed or removed.");
            }

            var modified = db.ChangeTracker.Entries<RelationCertificate>().Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count > 0)
            {
                var ids = modified.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.RelationCertificates.AsNoTracking().Where(c => ids.Contains(c.Id))
                        .Select(c => new { c.Id, c.RelationId, c.SourceVendorGgnId }), async, cancellationToken))
                    .ToDictionary(c => c.Id);
                foreach (var entry in modified)
                {
                    if (stored.TryGetValue(entry.Entity.Id, out var before)
                        && (before.RelationId != entry.Entity.RelationId || before.SourceVendorGgnId != entry.Entity.SourceVendorGgnId))
                    {
                        throw new TraceabilityViolationException($"Certificate {before.Id} stays with its relation (or the own company).");
                    }
                }
            }

            var added = db.ChangeTracker.Entries<RelationCertificateFile>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (added.Count == 0) return;
            // Een bestand bij een nieuw certificaat (nog zonder id) begint bij versie 1.
            foreach (var group in added.GroupBy(f => f.Certificate is { Id: 0 } fresh ? (object)fresh : f.Certificate?.Id ?? f.RelationCertificateId))
            {
                var first = group.First();
                var certificateId = first.Certificate is { Id: 0 } ? 0 : first.Certificate?.Id ?? first.RelationCertificateId;
                var highest = certificateId == 0 ? 0
                    : (await ToListAsync(db.RelationCertificateFiles.AsNoTracking().Where(f => f.RelationCertificateId == certificateId).Select(f => f.Version), async, cancellationToken))
                        .DefaultIfEmpty(0).Max();
                var versions = group.Select(f => f.Version).OrderBy(v => v).ToList();
                for (var i = 0; i < versions.Count; i++)
                {
                    if (versions[i] != highest + i + 1)
                    {
                        throw new TraceabilityViolationException($"A new certificate file is the next version ({highest + i + 1}), not version {versions[i]}.");
                    }
                }
            }
        }
    }
}
