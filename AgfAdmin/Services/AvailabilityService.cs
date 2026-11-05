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

            var variantFilter = query.ProductVariantId == null ? null : new[] { query.ProductVariantId.Value };
            var (sources, contents) = await LoadFactsAsync(_db, variantFilter, cancellationToken);
            var variants = AvailabilityCalculator.BuildOverview(sources, contents, query);
            // De verkoopstand hoort bij de eigen magazijnvoorraad; met het filter route=DirectTrade is er dus geen.
            if (query.Route != SupplyRules.RouteDirectTrade)
            {
                var lines = await SalesBalanceLoader.LoadSalesLinesAsync(_db, variantFilter, cancellationToken);
                variants = WithSales(variants, SalesBalanceCalculator.Build(sources, contents, lines), query);
            }
            return ServiceResult<AvailabilityOverviewDto>.Ok(new AvailabilityOverviewDto
            {
                GeneratedAt = DateTime.UtcNow,
                Basis = new AvailabilityBasisDto { ReservationsApplied = true },
                Variants = variants
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
            var (sources, contents) = await LoadFactsAsync(_db, new[] { productVariantId }, cancellationToken);
            var (sourceLines, contentLines) = AvailabilityCalculator.BuildDrillDown(sources, contents, query);
            // Verkoopgegevens horen bij de eigen magazijnvoorraad; bij Route=DirectTrade blijven ze leeg (zoals in het overzicht).
            var balances = new List<SalesBalance>();
            if (query.Route != SupplyRules.RouteDirectTrade)
            {
                var salesLines = await SalesBalanceLoader.LoadSalesLinesAsync(_db, new[] { productVariantId }, cancellationToken);
                balances = SalesBalanceCalculator.Build(sources, contents, salesLines).Values.ToList();
            }
            balances = balances
                .Where(b => query.Unit == null || b.Unit == query.Unit)
                .OrderBy(b => AvailabilityRules.UnitOrder(b.Unit)).ThenBy(b => b.Unit, StringComparer.Ordinal)
                .ToList();

            var drillDown = new AvailabilityDrillDownDto
            {
                GeneratedAt = DateTime.UtcNow,
                Basis = new AvailabilityBasisDto { ReservationsApplied = true },
                ProductVariantId = variant.Id,
                ProductName = variant.ProductName,
                VariantName = variant.VariantName,
                SourceLines = sourceLines,
                ContentLines = contentLines,
                Sales = balances.Select(SalesBalanceLoader.ToBalanceDto).ToList(),
                SalesLines = balances.SelectMany(b => b.Lines
                    .OrderBy(l => l.RequestedDeliveryDate).ThenBy(l => l.OrderCreatedAt).ThenBy(l => l.SalesOrderLineId)
                    .Select(l => new AvailabilitySalesLineDto
                    {
                        SalesOrderId = l.SalesOrderId,
                        OrderNumber = l.OrderNumber,
                        CustomerId = l.CustomerId,
                        CustomerName = l.CustomerName,
                        CustomerReference = l.CustomerReference,
                        RequestedDeliveryDate = l.RequestedDeliveryDate,
                        SalesOrderLineId = l.SalesOrderLineId,
                        Unit = l.Unit,
                        SupplySource = l.SupplySource,
                        OpenQuantity = l.OpenDemand,
                        ReservedQuantity = l.Reserved,
                        Coverage = SalesBalanceLoader.ToCoverageDto(b.Coverage[l.SalesOrderLineId])
                    })).ToList(),
                ExpectedSupplies = balances.SelectMany(b => b.Supplies
                    .OrderBy(e => e.ExpectedAt == null).ThenBy(e => e.ExpectedAt).ThenBy(e => e.SupplyCommitmentLineId)
                    .Select(e => new AvailabilityExpectedSupplyDto
                    {
                        SupplyCommitmentId = e.SupplyCommitmentId,
                        SupplyCommitmentLineId = e.SupplyCommitmentLineId,
                        SupplyOrderNumber = e.OrderNumber,
                        ExpectedAt = e.ExpectedAt == null ? null : DateTime.SpecifyKind(e.ExpectedAt.Value, DateTimeKind.Utc),
                        LotNumber = e.LotNumber,
                        Unit = b.Unit,
                        NotReceived = e.NotReceived,
                        InInspection = e.InInspection
                    })).ToList()
            };
            await AddPartijenAsync(drillDown, cancellationToken);
            return ServiceResult<AvailabilityDrillDownDto>.Ok(drillDown);
        }

        // Uitsplitsing per partij: het partijnummer uit de partij zelf (en haar id voor de link naar het partijdetail), naast het oude
        // tekstveld LotNumber, waarop de filter blijft werken.
        private async Task AddPartijenAsync(AvailabilityDrillDownDto drillDown, CancellationToken cancellationToken)
        {
            var lineIds = drillDown.SourceLines.Select(l => l.SupplyCommitmentLineId).Concat(drillDown.ExpectedSupplies.Select(e => e.SupplyCommitmentLineId)).Distinct().ToList();
            var contentIds = drillDown.ContentLines.Select(c => c.PalletContentLineId).ToList();
            var lines = await _db.SupplyCommitmentLines.AsNoTracking().Where(l => lineIds.Contains(l.Id) && l.PartijId != null)
                .Select(l => new { l.Id, l.PartijId, l.Partij.Number }).ToDictionaryAsync(l => l.Id, cancellationToken);
            var contents = await _db.PalletContentLines.AsNoTracking().Where(c => contentIds.Contains(c.Id) && c.PartijId != null)
                .Select(c => new { c.Id, c.PartijId, c.Partij.Number }).ToDictionaryAsync(c => c.Id, cancellationToken);
            foreach (var line in drillDown.SourceLines.Where(l => lines.ContainsKey(l.SupplyCommitmentLineId)))
            {
                (line.PartijId, line.PartijNumber) = (lines[line.SupplyCommitmentLineId].PartijId, lines[line.SupplyCommitmentLineId].Number);
            }
            foreach (var supply in drillDown.ExpectedSupplies.Where(e => lines.ContainsKey(e.SupplyCommitmentLineId)))
            {
                (supply.PartijId, supply.PartijNumber) = (lines[supply.SupplyCommitmentLineId].PartijId, lines[supply.SupplyCommitmentLineId].Number);
            }
            foreach (var content in drillDown.ContentLines.Where(c => contents.ContainsKey(c.PalletContentLineId)))
            {
                (content.PartijId, content.PartijNumber) = (contents[content.PalletContentLineId].PartijId, contents[content.PalletContentLineId].Number);
            }
        }

        // Zet de verkoopstand op elke magazijnrij (variant + eenheid). Een variant of eenheid die alleen verkocht is, krijgt een eigen rij
        // met nullen en alleen deze stand, zodat een tekort nooit onzichtbaar is. Klantfilter beperkt alleen DirectTrade (zoals hierboven).
        private static List<VariantAvailabilityDto> WithSales(List<VariantAvailabilityDto> variants, Dictionary<(int ProductVariantId, string Unit), SalesBalance> balances, AvailabilityQuery query)
        {
            var byId = variants.ToDictionary(v => v.ProductVariantId);
            foreach (var balance in balances.Values.Where(b => query.Unit == null || b.Unit == query.Unit))
            {
                if (!byId.TryGetValue(balance.ProductVariantId, out var variant))
                {
                    variant = new VariantAvailabilityDto { ProductVariantId = balance.ProductVariantId, ProductName = balance.ProductName, VariantName = balance.VariantName };
                    byId[balance.ProductVariantId] = variant;
                    variants.Add(variant);
                }
                var row = variant.WarehouseSupply.FirstOrDefault(r => string.Equals(r.Unit, balance.Unit, StringComparison.Ordinal));
                if (row == null)
                {
                    row = new WarehouseSupplyDto { Unit = balance.Unit };
                    variant.WarehouseSupply.Add(row);
                    variant.WarehouseSupply = variant.WarehouseSupply
                        .OrderBy(r => AvailabilityRules.UnitOrder(r.Unit)).ThenBy(r => r.Unit == null).ThenBy(r => r.Unit, StringComparer.Ordinal).ToList();
                }
                row.Sales = SalesBalanceLoader.ToBalanceDto(balance);
            }
            return variants
                .OrderBy(v => v.ProductName, StringComparer.Ordinal)
                .ThenBy(v => v.VariantName, StringComparer.Ordinal)
                .ThenBy(v => v.ProductVariantId)
                .ToList();
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

        // Ook gebruikt door de verkoopstand (SalesBalanceLoader): één definitie van de feiten. variantIds null = alle varianten.
        internal static async Task<(List<SourceLineFact> Sources, List<ContentLineFact> Contents)> LoadFactsAsync(AppDbContext db, IReadOnlyCollection<int> variantIds, CancellationToken cancellationToken)
        {
            var sourceQuery = db.SupplyCommitmentLines.AsNoTracking();
            var contentQuery = db.PalletContentLines.AsNoTracking();
            if (variantIds != null)
            {
                sourceQuery = sourceQuery.Where(l => variantIds.Contains(l.ProductVariantId));
                contentQuery = contentQuery.Where(c => variantIds.Contains(c.SupplyCommitmentLine.ProductVariantId));
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
                    l.ExpectedNetWeightKg,
                    l.SupplyCommitment.OrderNumber))
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
            var reservationQuery = db.StockReservations.AsNoTracking()
                .Where(r => r.PalletContentLineId != null && r.SalesOrderLineId != null
                    && r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourcePhysical);
            if (variantIds != null)
            {
                reservationQuery = reservationQuery.Where(r => variantIds.Contains(r.PalletContentLine.SupplyCommitmentLine.ProductVariantId));
            }
            var reservations = await reservationQuery
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
