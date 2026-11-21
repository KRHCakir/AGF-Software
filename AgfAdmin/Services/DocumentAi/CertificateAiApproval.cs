using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.DocumentAi
{
    // Goedkeuren van een AI-voorstel voor een certificaat gebeurt in het gewone opslagpad van de certificaten (CertificateService.SaveAsync),
    // in dezelfde transactie: de lezing wordt "goedgekeurd door" de ingelogde gebruiker (nooit van de client), met de goedgekeurde waarden en
    // wat de gebruiker vóór goedkeuren aanpaste (voorstel en eindwaarde per veld); het originele document wordt de volgende versie van het
    // bestand van dat certificaat, met hash en een verwijzing naar de lezing.
    public static class CertificateAiApproval
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public sealed record Prepared(AiExtraction Extraction, AiCertificateProposalDto Proposal, int Index);

        // Vóór het opslaan: bestaat de lezing, is het een open voorstel voor deze eigenaar, en wijst de index naar een regel van de lijst? Het
        // voorstel wordt hier opgebouwd (zoals de gebruiker het zag), vóór de opslag de lijst van de eigenaar verandert.
        public static async Task<Prepared> CheckAsync(AppDbContext db, CertificateAiApprovalDto approval, int? relationId, int count, Dictionary<string, List<string>> errors,
            CancellationToken cancellationToken)
        {
            if (approval == null) return null;
            if (approval.CertificateIndex < 0 || approval.CertificateIndex >= count)
            {
                ValidationErrors.Add(errors, "aiApproval", "The approved certificate is not in the list.");
                return null;
            }
            var extraction = await db.AiExtractions.AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == approval.ExtractionId && e.DocumentType == AiReadingRules.DocumentCertificate, cancellationToken);
            if (extraction == null || extraction.RelationId != relationId)
            {
                ValidationErrors.Add(errors, "aiApproval", $"AI reading {approval.ExtractionId} does not exist for this relation.");
                return null;
            }
            if (extraction.Status != AiReadingRules.StatusProposed || extraction.FileDeletedAt != null || string.IsNullOrEmpty(extraction.ProposalJson))
            {
                ValidationErrors.Add(errors, "aiApproval", $"AI reading {extraction.Id} is {extraction.Status.ToLowerInvariant()}; only an open proposal can be approved.");
                return null;
            }
            var context = await CertificateProposalContext.LoadAsync(db, relationId, cancellationToken);
            return new Prepared(extraction, context.Build((JsonObject)JsonNode.Parse(extraction.ProposalJson), out _), approval.CertificateIndex);
        }

        // Na het opslaan van de lijst (het certificaat heeft een id): claimt de lezing atomair (UPDATE ... WHERE Status = 'Proposed') en voegt
        // de versie toe. Een foutmelding betekent: de lezing was intussen al goedgekeurd of gesloten; de aanroeper rolt dan alles terug.
        public static async Task<string> ApplyAsync(AppDbContext db, Prepared prepared, RelationCertificate certificate, SaveCertificateDto item,
            string actor, DateTime now, CancellationToken cancellationToken)
        {
            var (extraction, proposal) = (prepared.Extraction, prepared.Proposal);
            var adjustments = JsonSerializer.Serialize(Adjustments(proposal, certificate, item), Json);
            var approved = JsonSerializer.Serialize(new
            {
                certificateId = certificate.Id, typeCode = certificate.TypeCode, customTypeName = certificate.CustomTypeName, number = certificate.Number,
                certificationBody = certificate.CertificationBody, validFrom = certificate.ValidFrom, validUntil = certificate.ValidUntil, producerName = certificate.ProducerName,
                productIds = item.ProductIds ?? new List<int>(), checkedOn = certificate.CheckedOn, isActive = certificate.IsActive,
            }, Json);
            var claimed = await db.AiExtractions.Where(e => e.Id == extraction.Id && e.Status == AiReadingRules.StatusProposed && e.FileDeletedAt == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.Status, AiReadingRules.StatusApproved)
                    .SetProperty(e => e.ApprovedBy, actor)
                    .SetProperty(e => e.ApprovedAt, now)
                    .SetProperty(e => e.RelationCertificateId, certificate.Id)
                    .SetProperty(e => e.ApprovedValuesJson, approved)
                    .SetProperty(e => e.AdjustmentsJson, adjustments), cancellationToken);
            if (claimed == 0) return $"AI reading {extraction.Id} has already been approved or closed; only an open proposal can be approved.";
            var version = (await db.RelationCertificateFiles.Where(f => f.RelationCertificateId == certificate.Id).Select(f => (int?)f.Version).MaxAsync(cancellationToken) ?? 0) + 1;
            db.RelationCertificateFiles.Add(new RelationCertificateFile
            {
                RelationCertificateId = certificate.Id, Version = version, FileName = extraction.FileName, MediaType = extraction.MediaType, SizeBytes = extraction.SizeBytes,
                Sha256 = extraction.Sha256, StorageKey = extraction.StorageKey, DisplayStorageKey = extraction.DisplayStorageKey, DisplayMediaType = extraction.DisplayMediaType,
                CreatedAt = now, CreatedBy = actor, AiExtractionId = extraction.Id,
            });
            return null;
        }

        // Wat de AI voorstelde tegenover wat de gebruiker goedkeurde, per veld dat verschilt ("door gebruiker aangepast").
        public static List<AiAdjustmentDto> Adjustments(AiCertificateProposalDto proposal, RelationCertificate certificate, SaveCertificateDto item)
        {
            var list = new List<AiAdjustmentDto>();
            void Compare(string field, string proposed, string final)
            {
                if (!string.Equals(Blank(proposed), Blank(final), StringComparison.Ordinal)) list.Add(new AiAdjustmentDto(field, proposed, final));
            }
            static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Compare("typeCode", proposal.TypeCode, certificate.TypeCode);
            if (certificate.TypeCode == CertificateRules.Custom) Compare("customTypeName", proposal.CustomTypeName, certificate.CustomTypeName);
            Compare("number", proposal.Number, certificate.Number);
            Compare("certificationBody", proposal.CertificationBody, certificate.CertificationBody);
            Compare("validFrom", Date(proposal.ValidFrom), Date(certificate.ValidFrom));
            Compare("validUntil", Date(proposal.ValidUntil), Date(certificate.ValidUntil));
            if (CertificateRules.IsGgnType(certificate.TypeCode)) Compare("producerName", proposal.HolderName, certificate.ProducerName);
            static string Ids(IEnumerable<int> ids) => string.Join(",", ids.Distinct().OrderBy(id => id));
            Compare("products", Ids(proposal.Products.Where(p => p.ProductId != null).Select(p => p.ProductId.Value)), Ids(item.ProductIds ?? new List<int>()));
            // Een nieuwe versie van een bestaand certificaat in plaats van een nieuw certificaat (of andersom): ook een keuze van de gebruiker.
            Compare("certificate", proposal.SameNumberCertificateId?.ToString(CultureInfo.InvariantCulture), item.Id?.ToString(CultureInfo.InvariantCulture));
            return list;
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
