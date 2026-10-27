using AgfAdmin.Data;
using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Alleen lezen. Bronnen: SupplyCommitmentLines, PalletContentLines en Active Physical verkoopreserveringen (SalesOrderLineId).
    // Bewust niet gelezen: legacy Inventory/Shipment, StockMovements/StockReceipts en legacy OrderLineId-reserveringen.
    // SQL laadt alleen gefilterde platte projecties; alle optellingen gebeuren exact in AvailabilityCalculator.
    public sealed class AvailabilityService : IAvailabilityService
    {
        private readonly AppDbContext _db;

        public AvailabilityService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<ServiceResult<AvailabilityOverviewDto>> GetOverviewAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
        {
            query ??= new AvailabilityQuery();
            var errors = ValidateCommonFilters(query);
            if (errors.Count > 0)
            {
                return ServiceResult<AvailabilityOverviewDto>.Validation(errors);
            }

            var (sources, contents) = await LoadFactsAsync(query.ProductVariantId, cancellationToken);
            return ServiceResult<AvailabilityOverviewDto>.Ok(new AvailabilityOverviewDto
            {
                GeneratedAt = DateTime.UtcNow,
                Basis = new AvailabilityBasisDto { ReservationsApplied = true },
                Variants = AvailabilityCalculator.BuildOverview(sources, contents, query)
            });
        }

        public async Task<ServiceResult<AvailabilityDrillDownDto>> GetVariantAsync(int productVariantId, AvailabilityQuery query, CancellationToken cancellationToken = default)
        {
            query ??= new AvailabilityQuery();
            var errors = ValidateCommonFilters(query);
            if (query.Lot != null)
            {
                if (string.IsNullOrWhiteSpace(query.Lot))
                {
                    errors["lot"] = new[] { "Lot must not be blank." };
                }
                else
                {
                    query.Lot = query.Lot.Trim();
                }
            }
            if (query.Category != null && !AvailabilityRules.AllCategories.Contains(query.Category, StringComparer.Ordinal))
            {
                errors["category"] = new[] { $"Category must be one of: {string.Join(", ", AvailabilityRules.AllCategories)}." };
            }
            else if (query.Category == AvailabilityRules.CategoryClosed && !query.IncludeClosed)
            {
                errors["category"] = new[] { "Category closed requires includeClosed=true." };
            }
            if (query.QualityStatus != null && !PalletRules.IsQualityStatus(query.QualityStatus))
            {
                errors["qualityStatus"] = new[] { $"Quality status must be one of: {string.Join(", ", PalletRules.QualityStatuses)}." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<AvailabilityDrillDownDto>.Validation(errors);
            }

            var variant = await _db.ProductVariants
                .AsNoTracking()
                .Where(v => v.Id == productVariantId)
                .Select(v => new { v.Id, ProductName = v.Product.Name, VariantName = v.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (variant == null)
            {
                return ServiceResult<AvailabilityDrillDownDto>.NotFound($"Product variant {productVariantId} does not exist.");
            }

            query.ProductVariantId = productVariantId;
            var (sources, contents) = await LoadFactsAsync(productVariantId, cancellationToken);
            var (sourceLines, contentLines) = AvailabilityCalculator.BuildDrillDown(sources, contents, query);

            return ServiceResult<AvailabilityDrillDownDto>.Ok(new AvailabilityDrillDownDto
            {
                GeneratedAt = DateTime.UtcNow,
                Basis = new AvailabilityBasisDto { ReservationsApplied = true },
                ProductVariantId = variant.Id,
                ProductName = variant.ProductName,
                VariantName = variant.VariantName,
                SourceLines = sourceLines,
                ContentLines = contentLines
            });
        }

        private static Dictionary<string, string[]> ValidateCommonFilters(AvailabilityQuery query)
        {
            var errors = new Dictionary<string, string[]>();
            if (query.Route != null && !SupplyRules.IsRoute(query.Route))
            {
                errors["route"] = new[] { $"Route must be one of: {string.Join(", ", SupplyRules.Routes)}." };
            }
            if (query.Unit != null && !SupplyRules.IsPackageUnit(query.Unit))
            {
                errors["unit"] = new[] { $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}." };
            }
            return errors;
        }

        private async Task<(List<SourceLineFact> Sources, List<ContentLineFact> Contents)> LoadFactsAsync(int? productVariantId, CancellationToken cancellationToken)
        {
            var sourceQuery = _db.SupplyCommitmentLines.AsNoTracking();
            var contentQuery = _db.PalletContentLines.AsNoTracking();
            if (productVariantId != null)
            {
                sourceQuery = sourceQuery.Where(l => l.ProductVariantId == productVariantId);
                contentQuery = contentQuery.Where(c => c.SupplyCommitmentLine.ProductVariantId == productVariantId);
            }

            var sources = await sourceQuery
                .Select(l => new SourceLineFact(
                    l.Id,
                    l.SupplyCommitmentId,
                    l.SupplyCommitment.Route,
                    l.SupplyCommitment.CustomerId,
                    l.SupplyCommitment.Customer.Name,
                    l.SupplyCommitment.Status,
                    l.SupplyCommitment.ExpectedAt,
                    l.ProductVariantId,
                    l.ProductVariant.Product.Name,
                    l.ProductVariant.Name,
                    l.LotNumber,
                    l.ExpectedUnit,
                    l.ExpectedQuantity,
                    l.ExpectedNetWeightKg))
                .ToListAsync(cancellationToken);

            var contents = await contentQuery
                .Select(c => new ContentLineFact(
                    c.Id,
                    c.PalletId,
                    c.Pallet.Reference,
                    c.Pallet.LogisticsStatus,
                    c.Pallet.LocationContext,
                    c.Pallet.WarehouseLocation,
                    c.SupplyCommitmentLineId,
                    c.SupplyCommitmentLine.SupplyCommitment.Route,
                    c.SupplyCommitmentLine.SupplyCommitment.CustomerId,
                    c.SupplyCommitmentLine.SupplyCommitment.Customer.Name,
                    c.SupplyCommitmentLine.ProductVariantId,
                    c.SupplyCommitmentLine.ProductVariant.Product.Name,
                    c.SupplyCommitmentLine.ProductVariant.Name,
                    c.LotNumber,
                    c.Unit,
                    c.Quantity,
                    c.NetWeightKg,
                    c.QualityStatus,
                    0m,
                    0m,
                    c.DispatchedQuantity,
                    c.DispatchedNetWeightKg,
                    c.OriginCorrectionLineId != null,
                    c.WeightMethod,
                    c.NetKgPerPackage,
                    c.Ggn,
                    c.SplitOffQuantity,
                    c.SplitOffNetWeightKg,
                    c.OriginRejectionLineId != null))
                .ToListAsync(cancellationToken);

            // Alleen Active Physical verkoopreserveringen (SalesOrderLineId); legacy OrderLineId-reserveringen nooit.
            var reservations = await _db.StockReservations.AsNoTracking()
                .Where(r => r.PalletContentLineId != null && r.SalesOrderLineId != null
                    && r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourcePhysical
                    && (productVariantId == null || r.PalletContentLine.SupplyCommitmentLine.ProductVariantId == productVariantId))
                .Select(r => new { ContentId = r.PalletContentLineId!.Value, r.Quantity, r.NetWeightKg })
                .ToListAsync(cancellationToken);
            var reserved = reservations
                .GroupBy(r => r.ContentId)
                .ToDictionary(g => g.Key, g => (Quantity: g.Sum(r => r.Quantity), Kg: g.Sum(r => r.NetWeightKg ?? 0m)));
            contents = contents
                .Select(c => reserved.TryGetValue(c.PalletContentLineId, out var r) ? c with { ReservedQuantity = r.Quantity, ReservedNetWeightKg = r.Kg } : c)
                .ToList();

            return (sources, contents);
        }
    }
}
