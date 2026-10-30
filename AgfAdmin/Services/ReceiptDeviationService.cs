using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Afwijkende ontvangst binnen een bestaande inkooporder (alleen lezen hier): welke varianten als "ander artikel" gekozen kunnen
    // worden en wat "Ja, klopt" zou doen. Het boeken zelf gebeurt in PalletService.AddContentLineAsync (zelfde aanvraag als een gewone
    // ontvangst, met ReceivedProductVariantId en ConfirmDeviation), zodat er nooit een tweede weg langs de bevestiging is.
    public sealed class ReceiptDeviationService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public ReceiptDeviationService(AppDbContext db)
        {
            _db = db;
        }

        // Alle andere varianten van hetzelfde product, met de afwijkende kenmerken; een ander ras/naam is gemarkeerd als ander artikel.
        public async Task<ServiceResult<IReadOnlyList<ReceiptDeviationCandidateDto>>> CandidatesAsync(int lineId, CancellationToken cancellationToken = default)
        {
            var line = await _db.SupplyCommitmentLines.AsNoTracking().Select(l => new { l.Id, l.ProductVariantId }).SingleOrDefaultAsync(l => l.Id == lineId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<IReadOnlyList<ReceiptDeviationCandidateDto>>.NotFound($"Supply commitment line {lineId} does not exist.");
            }
            var expected = await VariantAsync(line.ProductVariantId, cancellationToken);
            var variants = await _db.ProductVariants.AsNoTracking()
                .Where(v => v.ProductId == expected.ProductId && v.Id != expected.Id)
                .Select(v => new { Facts = new ReceiptDeviationRules.VariantFacts(v.Id, v.ProductId, v.Product.Name, v.Name, v.Origin, v.SizeCategory, v.Packaging, v.PackagingUnit, v.TradeClass, v.Brand, v.OriginCountryCode), v.ShortcutCode })
                .ToListAsync(cancellationToken);
            return ServiceResult<IReadOnlyList<ReceiptDeviationCandidateDto>>.Ok(variants
                .OrderBy(v => v.Facts.Name).ThenBy(v => v.Facts.Origin).ThenBy(v => v.Facts.Size).ThenBy(v => v.Facts.Id)
                .Select(v => new ReceiptDeviationCandidateDto
                {
                    Variant = ToDto(v.Facts),
                    ShortcutCode = v.ShortcutCode,
                    OtherArticle = !ReceiptDeviationRules.IsSameArticle(expected, v.Facts),
                    Kinds = ReceiptDeviationRules.DifferingKinds(expected, v.Facts).ToList()
                })
                .ToList());
        }

        // Wat "Ja, klopt" zou doen voor deze hoeveelheid, zonder iets te boeken. De client vraagt dit vóór het aanmaken van de pallet,
        // zodat "Nee" echt niets achterlaat; de server controleert bij het boeken alles opnieuw.
        public async Task<ServiceResult<ReceiptDeviationPreviewDto>> PreviewAsync(int lineId, int? receivedProductVariantId, decimal? quantity, decimal? netWeightKg = null, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (receivedProductVariantId == null) errors["receivedProductVariantId"] = new[] { "The received product variant is required." };
            if (quantity == null || !SupplyRules.IsPositiveDecimal18_3(quantity.Value)) errors["quantity"] = new[] { "Quantity is required, must be positive and may have at most 3 decimals." };
            if (netWeightKg != null && !SupplyRules.IsPositiveDecimal18_3(netWeightKg.Value)) errors["netWeightKg"] = new[] { "Net kg, when given, must be positive and may have at most 3 decimals." };
            if (errors.Count > 0)
            {
                return ServiceResult<ReceiptDeviationPreviewDto>.Validation(errors);
            }
            var line = await _db.SupplyCommitmentLines.AsNoTracking().Include(l => l.SupplyCommitment).SingleOrDefaultAsync(l => l.Id == lineId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<ReceiptDeviationPreviewDto>.NotFound($"Supply commitment line {lineId} does not exist.");
            }
            var plan = await PlanAsync(line, receivedProductVariantId!.Value, quantity!.Value, netWeightKg, cancellationToken);
            return plan == null
                ? ServiceResult<ReceiptDeviationPreviewDto>.NotFound($"Product variant {receivedProductVariantId} does not exist.")
                : ServiceResult<ReceiptDeviationPreviewDto>.Ok(plan.Preview);
        }

        // ---------------- Plan (ook gebruikt door PalletService) ----------------

        internal sealed record Plan(ReceiptDeviationPreviewDto Preview, ReceiptDeviationRules.VariantFacts Expected, ReceiptDeviationRules.VariantFacts Received,
            ReceiptDeviationRules.LineShape Before, ReceiptDeviationRules.LineShape After);

        // Null als de ontvangen variant niet bestaat. Status: Exact, Deviation, OtherArticle of BlockedAssigned.
        internal async Task<Plan> PlanAsync(SupplyCommitmentLine line, int receivedProductVariantId, decimal quantity, decimal? netWeightKg, CancellationToken cancellationToken)
        {
            var received = await VariantAsync(receivedProductVariantId, cancellationToken);
            if (received == null)
            {
                return null;
            }
            var expected = await VariantAsync(line.ProductVariantId, cancellationToken);
            var sameVariant = expected.Id == received.Id;
            var receivedOnLine = await ReceivedOnLineAsync(line.Id, line.ExpectedUnit, cancellationToken);
            // Alleen een gewichtsafwijking verandert het aantal van de regel niet; een andere variant verlaagt haar (nooit onder ontvangen).
            var reduced = sameVariant ? 0m : ReceiptDeviationRules.ReducedQuantity(line.ExpectedQuantity, receivedOnLine, quantity);
            decimal? assigned = null;
            if (line.SupplyCommitment.Route == SupplyRules.RouteDirectTrade)
            {
                assigned = (await DirectTradeAllocationService.AllocatedBySourceLineAsync(_db, new List<int> { line.Id }, cancellationToken)).GetValueOrDefault(line.Id);
            }

            var defaultKg = await _db.ProductVariants.AsNoTracking().Where(v => v.Id == line.ProductVariantId).Select(v => v.DefaultNetKgPerPackage).SingleAsync(cancellationToken);
            var expectedPerPackage = ReceiptDeviationRules.ExpectedKgPerPackage(line.ExpectedQuantity, line.ExpectedNetWeightKg, defaultKg);
            var receivedPerPackage = netWeightKg == null ? (decimal?)null : ReceiptDeviationRules.ReceivedKgPerPackage(quantity, netWeightKg.Value);
            var weight = netWeightKg != null && ReceiptDeviationRules.IsWeightDeviation(expectedPerPackage, quantity, netWeightKg.Value);
            var kinds = ReceiptDeviationRules.DifferingKinds(expected, received).ToList();
            if (weight) kinds.Add(ReceiptDeviationRules.KindWeight);

            var status = sameVariant ? (weight ? "Deviation" : "Exact")
                : !ReceiptDeviationRules.IsSameArticle(expected, received) ? "OtherArticle"
                // Klantgebonden aanvoer: de oorspronkelijke regel moet zijn toewijzingen blijven dragen (verhuizen is een volgende fase).
                : assigned != null && line.ExpectedQuantity - reduced < assigned.Value ? "BlockedAssigned"
                : "Deviation";
            var shape = new ReceiptDeviationRules.LineShape(line.ExpectedQuantity, line.ExpectedNetWeightKg, line.ExpectedGrossWeightKg, line.WeightSource,
                line.PalletCount, line.PackagesPerPallet, line.LoosePackages);
            var after = sameVariant
                ? (weight ? ReceiptDeviationRules.Reweigh(shape, receivedPerPackage!.Value) : shape)
                : ReceiptDeviationRules.Reduce(shape, reduced);
            return new Plan(new ReceiptDeviationPreviewDto
            {
                Status = status,
                SupplyCommitmentId = line.SupplyCommitmentId,
                OrderNumber = line.SupplyCommitment.OrderNumber,
                OriginalLineId = line.Id,
                Expected = ToDto(expected),
                Received = ToDto(received),
                Kinds = kinds,
                Quantity = quantity,
                Unit = line.ExpectedUnit,
                OriginalExpectedQuantity = line.ExpectedQuantity,
                OriginalReceivedQuantity = receivedOnLine,
                ReducedQuantity = reduced,
                OriginalExpectedQuantityAfter = line.ExpectedQuantity - reduced,
                ExcessQuantity = sameVariant ? 0m : quantity - reduced,
                AssignedQuantity = assigned,
                ExpectedKgPerPackage = expectedPerPackage == null ? null : decimal.Round(expectedPerPackage.Value, 3, MidpointRounding.AwayFromZero),
                ReceivedKgPerPackage = receivedPerPackage == null ? null : decimal.Round(receivedPerPackage.Value, 3, MidpointRounding.AwayFromZero),
                ExpectedNetKg = expectedPerPackage == null ? null : decimal.Round(expectedPerPackage.Value * quantity, 3, MidpointRounding.AwayFromZero),
                ReceivedNetKg = netWeightKg,
                OriginalExpectedNetKg = line.ExpectedNetWeightKg,
                OriginalExpectedNetKgAfter = after.NetKg
            }, expected, received, shape, after);
        }

        // Fysiek ontvangen op de regel, in de eenheid van de regel: zelfde telling als de orderstatus (PurchaseOrderService):
        // geen teruggeboekte of afgesplitste inhoud, geen geannuleerde pallet.
        internal async Task<decimal> ReceivedOnLineAsync(int lineId, string unit, CancellationToken cancellationToken)
        {
            var quantities = await _db.PalletContentLines.AsNoTracking()
                .Where(c => c.SupplyCommitmentLineId == lineId && c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null
                    && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled && (unit == null || c.Unit == unit))
                .Select(c => c.Quantity)
                .ToListAsync(cancellationToken);
            return quantities.Sum();
        }

        private Task<ReceiptDeviationRules.VariantFacts> VariantAsync(int id, CancellationToken cancellationToken) =>
            _db.ProductVariants.AsNoTracking()
                .Where(v => v.Id == id)
                .Select(v => new ReceiptDeviationRules.VariantFacts(v.Id, v.ProductId, v.Product.Name, v.Name, v.Origin, v.SizeCategory, v.Packaging, v.PackagingUnit, v.TradeClass, v.Brand, v.OriginCountryCode))
                .SingleOrDefaultAsync(cancellationToken);

        internal static ReceiptDeviationVariantDto ToDto(ReceiptDeviationRules.VariantFacts facts) => new()
        {
            ProductVariantId = facts.Id,
            ProductId = facts.ProductId,
            ProductName = facts.ProductName,
            VariantName = facts.Name,
            Origin = facts.Origin,
            OriginCountryCode = facts.OriginCountryCode,
            Size = facts.Size,
            Packaging = facts.Packaging,
            PackagingUnit = facts.PackagingUnit,
            TradeClass = facts.TradeClass,
            Brand = facts.Brand
        };

        internal static string Snapshot(object value) => JsonSerializer.Serialize(value, SnapshotJson);

        internal static ReceiptDeviationVariantDto ReadVariant(string snapshot)
        {
            try
            {
                return JsonSerializer.Deserialize<ReceiptDeviationVariantDto>(snapshot, SnapshotJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
