using System.Text.Json;
using System.Text.Json.Nodes;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.DocumentAi
{
    // Certificaten deel 2: een certificaat (PDF of foto) uploaden en door AI laten voorinvullen, bij een relatie of bij het eigen bedrijf
    // (relationId null). Hetzelfde leespad als de inkoopfactuur (zelfde instelling aan/uit, maandlimiet, uploads per minuut, vastlegging met
    // model, tokens en kosten), met het schema van een certificaat. De AI slaat niets op: opslaan gebeurt pas bij Goedkeuren, via
    // PUT api/certificates/... met aiApproval (CertificateService). Afwijzen en Handmatig verder sluiten het voorstel zonder iets op te slaan.
    public sealed partial class AiInvoiceReadingService
    {
        public async Task<ServiceResult<AiCertificateExtractionDto>> ReadCertificateAsync(int? relationId, string fileName, byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (relationId != null && !await db.Relations.AnyAsync(r => r.Id == relationId, cancellationToken))
            {
                return ServiceResult<AiCertificateExtractionDto>.NotFound($"Relation {relationId} does not exist.");
            }
            var gate = await GateAsync(fileName, bytes, cancellationToken);
            if (gate.Validation != null) return ServiceResult<AiCertificateExtractionDto>.Validation(gate.Validation);
            if (gate.Code != null) return Refused(gate.Code, gate.Message).As<AiCertificateExtractionDto>();
            var (extraction, failure) = await ReadAndRecordAsync(fileName, gate.Document, AiReadingRules.DocumentCertificate, relationId,
                document => reader.ReadCertificateAsync(document, CancellationToken.None), cancellationToken);
            if (failure != null) return Refused(FailureCode(failure), FailureMessage(failure, "het certificaat")).As<AiCertificateExtractionDto>();
            var dto = await CertificateDtoAsync(extraction, cancellationToken);
            // De controles van dit moment bij de lezing (voor later nazoeken); het scherm rekent ze bij elke keer openen opnieuw uit.
            extraction.ProblemsJson = JsonSerializer.Serialize(dto.Problems, Json);
            await db.SaveChangesAsync(CancellationToken.None);
            return ServiceResult<AiCertificateExtractionDto>.Ok(dto);
        }

        public async Task<ServiceResult<AiCertificateExtractionDto>> GetCertificateAsync(int id, CancellationToken cancellationToken = default)
        {
            var extraction = await db.AiExtractions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.DocumentType == AiReadingRules.DocumentCertificate, cancellationToken);
            return extraction == null ? ServiceResult<AiCertificateExtractionDto>.NotFound($"AI reading {id} does not exist.")
                : ServiceResult<AiCertificateExtractionDto>.Ok(await CertificateDtoAsync(extraction, cancellationToken));
        }

        public Task<(byte[] Bytes, string MediaType, string FileName)?> CertificateDocumentAsync(int id, CancellationToken cancellationToken = default) =>
            DocumentAsync(id, AiReadingRules.DocumentCertificate, cancellationToken);

        public async Task<ServiceResult<AiCertificateExtractionDto>> CloseCertificateAsync(int id, CloseAiExtractionDto request, CancellationToken cancellationToken = default)
        {
            var (extraction, refused) = await CloseCoreAsync(id, request, AiReadingRules.DocumentCertificate, cancellationToken);
            return refused != null ? refused.As<AiCertificateExtractionDto>() : ServiceResult<AiCertificateExtractionDto>.Ok(await CertificateDtoAsync(extraction, cancellationToken));
        }

        private async Task<AiCertificateExtractionDto> CertificateDtoAsync(AiExtraction extraction, CancellationToken cancellationToken)
        {
            AiCertificateProposalDto proposal = null;
            var problems = new List<AiProblemDto>();
            if (extraction.ProposalJson != null && JsonNode.Parse(extraction.ProposalJson) is JsonObject json)
            {
                proposal = (await CertificateProposalContext.LoadAsync(db, extraction.RelationId, cancellationToken)).Build(json, out problems);
            }
            return new AiCertificateExtractionDto
            {
                Id = extraction.Id, Status = extraction.Status, RelationId = extraction.RelationId, FileName = extraction.FileName, MediaType = extraction.MediaType,
                DisplayMediaType = extraction.DisplayMediaType ?? extraction.MediaType, SizeBytes = extraction.SizeBytes, Provider = extraction.Provider, Model = extraction.Model,
                ModelName = extraction.Model == null ? null : _options.Models.GetValueOrDefault(extraction.Model)?.DisplayName ?? AiModelNames.Display(extraction.Model),
                ModelVersion = extraction.ModelVersion, UsedFallback = extraction.UsedFallback, CostEur = decimal.Round(extraction.CostEur, 4), Proposal = proposal, Problems = problems,
                RelationCertificateId = extraction.RelationCertificateId, ApprovedBy = extraction.ApprovedBy,
                ApprovedAt = extraction.ApprovedAt == null ? null : DateTime.SpecifyKind(extraction.ApprovedAt.Value, DateTimeKind.Utc),
            };
        }
    }
}
