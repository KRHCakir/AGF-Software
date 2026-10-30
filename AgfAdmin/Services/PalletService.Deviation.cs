using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Afwijkende ontvangst binnen een bestaande order (zie ReceiptDeviationRules). Na "Ja, klopt" in dezelfde save als de inhoudsregel:
    // een vervangende bronregel voor de ontvangen variant (verwacht = ontvangen hoeveelheid, ReplacesLineId = oorspronkelijke regel),
    // de oorspronkelijke regel omlaag (nooit onder wat er al ontvangen is), de inhoud aan de vervangende regel en een afwijkingsrecord.
    // Zonder bevestiging, bij een ander artikel of bij klantgebonden aanvoer die geraakt zou worden: 409 met code en het verschil.
    public sealed partial class PalletService
    {
        private sealed record DeviationOutcome(SupplyReceiptDeviation Deviation, ServiceResult<PalletContentLineDto> Refused);

        private async Task<DeviationOutcome> PlanDeviationAsync(SupplyCommitmentLine source, PalletContentLine candidate, AddPalletContentLineDto request, string actor, CancellationToken cancellationToken)
        {
            var plan = await new ReceiptDeviationService(_db).PlanAsync(source, request.ReceivedProductVariantId!.Value, candidate.Quantity, cancellationToken);
            if (plan == null)
            {
                return Refuse(ServiceResult<PalletContentLineDto>.NotFound($"Product variant {request.ReceivedProductVariantId} does not exist."));
            }
            var preview = plan.Preview;
            if (preview.Status == "OtherArticle")
            {
                return Refuse(DeviationConflict(ReceiptDeviationRules.CodeOtherArticle, preview,
                    "This is a different article (other product or variety), not a deviation; record a new supply line for it."));
            }
            if (preview.Status == "BlockedAssigned")
            {
                return Refuse(DeviationConflict(ReceiptDeviationRules.CodeBlockedAssigned, preview,
                    $"This supply is already assigned to a customer ({preview.AssignedQuantity} {preview.Unit}); resolve that first. Moving assignments to another variant is not supported yet."));
            }
            if (request.ConfirmDeviation != true)
            {
                return Refuse(DeviationConflict(ReceiptDeviationRules.CodeConfirmationRequired, preview,
                    $"Order {preview.OrderNumber} expects product variant {preview.Expected.ProductVariantId}, received {preview.Received.ProductVariantId} ({string.Join(", ", preview.Kinds)}); confirm the deviation."));
            }

            // Gevolgd geladen: de versie van de oorspronkelijke regel is het concurrency-token tegen een gelijktijdige afwijking of toewijzing.
            var original = await _db.SupplyCommitmentLines.SingleAsync(l => l.Id == source.Id, cancellationToken);
            if (original.Version != source.Version || original.ExpectedQuantity != preview.OriginalExpectedQuantity)
            {
                return Refuse(ServiceResult<PalletContentLineDto>.Conflict(ConcurrentChangeMessage($"Supply commitment line {source.Id}")));
            }
            var before = new ReceiptDeviationRules.LineShape(original.ExpectedQuantity, original.ExpectedNetWeightKg, original.ExpectedGrossWeightKg,
                original.WeightSource, original.PalletCount, original.PackagesPerPallet, original.LoosePackages);
            var after = ReceiptDeviationRules.Reduce(before, preview.ReducedQuantity);
            if (preview.ReducedQuantity > 0m)
            {
                original.ExpectedQuantity = after.ExpectedQuantity;
                original.ExpectedNetWeightKg = after.NetKg;
                original.ExpectedGrossWeightKg = after.GrossKg;
                original.WeightSource = after.WeightSource;
                original.PalletCount = after.PalletCount;
                original.PackagesPerPallet = after.PackagesPerPallet;
                original.LoosePackages = after.LoosePackages;
            }
            else
            {
                // Niets te verlagen (alles al ontvangen): toch de versie ophogen, zodat twee afwijkingen op dezelfde regel elkaar uitsluiten.
                _db.Entry(original).Property(l => l.Version).IsModified = true;
            }

            // De vervangende regel verwacht precies wat er binnenkwam, met het werkelijke kg (opgegeven, niet afgeleid) en de partij.
            var replacement = new SupplyCommitmentLine
            {
                SupplyCommitmentId = original.SupplyCommitmentId,
                ProductVariantId = plan.Received.Id,
                ExpectedQuantity = candidate.Quantity,
                ExpectedUnit = original.ExpectedUnit ?? candidate.Unit,
                ExpectedNetWeightKg = candidate.NetWeightKg,
                WeightSource = LineWeightRules.Entered,
                LotNumber = candidate.LotNumber,
                ReplacesLineId = original.Id,
                CreatedAt = candidate.CreatedAt
            };
            _db.SupplyCommitmentLines.Add(replacement);
            candidate.SupplyCommitmentLineId = 0;
            candidate.SupplyCommitmentLine = replacement;

            var note = string.IsNullOrWhiteSpace(request.DeviationReasonNote) ? null : request.DeviationReasonNote.Trim();
            var deviation = new SupplyReceiptDeviation
            {
                RequestId = candidate.RequestId,
                SupplyCommitmentId = original.SupplyCommitmentId,
                OriginalLineId = original.Id,
                NewLine = replacement,
                PalletContentLine = candidate,
                Kinds = string.Join(",", preview.Kinds),
                ExpectedProductVariantId = plan.Expected.Id,
                ExpectedVariantSnapshot = ReceiptDeviationService.Snapshot(preview.Expected),
                ReceivedProductVariantId = plan.Received.Id,
                ReceivedVariantSnapshot = ReceiptDeviationService.Snapshot(preview.Received),
                Quantity = candidate.Quantity,
                Unit = replacement.ExpectedUnit,
                ReducedQuantity = preview.ReducedQuantity,
                OriginalLineBefore = ReceiptDeviationService.Snapshot(before),
                OriginalLineAfter = ReceiptDeviationService.Snapshot(preview.ReducedQuantity > 0m ? after : before),
                Reason = request.DeviationReason,
                // Een toelichting zonder reden hoort bij "Anders"; zonder reden geen losse toelichting.
                ReasonNote = request.DeviationReason == null ? null : note,
                ActorReference = actor,
                CreatedAt = candidate.CreatedAt
            };
            _db.SupplyReceiptDeviations.Add(deviation);
            return new DeviationOutcome(deviation, null);
        }

        private static DeviationOutcome Refuse(ServiceResult<PalletContentLineDto> result) => new(null, result);

        private static ServiceResult<PalletContentLineDto> DeviationConflict(string code, ReceiptDeviationPreviewDto preview, string message) =>
            ServiceResult<PalletContentLineDto>.Conflict(message, new ReceiptDeviationConflictDto { Message = message, Code = code, Deviation = preview });
    }
}
