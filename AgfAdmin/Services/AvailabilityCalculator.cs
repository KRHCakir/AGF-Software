using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // Invoerfeiten voor de berekening: platte projecties zonder EF, zodat de berekening puur en los testbaar is.
    public sealed record SourceLineFact(
        int SupplyCommitmentLineId,
        int SupplyCommitmentId,
        string Route,
        int? CustomerId,
        string CustomerName,
        string CommitmentStatus,
        DateTime? ExpectedAt,
        int ProductVariantId,
        string ProductName,
        string VariantName,
        string LotNumber,
        string Unit,
        decimal ExpectedQuantity,
        decimal? DeclaredExpectedNetWeightKg);

    public sealed record ContentLineFact(
        int PalletContentLineId,
        int PalletId,
        string PalletReference,
        string LogisticsStatus,
        string LocationContext,
        string WarehouseLocation,
        int SupplyCommitmentLineId,
        string Route,
        int? CustomerId,
        string CustomerName,
        int ProductVariantId,
        string ProductName,
        string VariantName,
        string LotNumber,
        string Unit,
        decimal Quantity,
        decimal NetWeightKg,
        string QualityStatus);

    // Pure berekening in het geheugen (decimal, dus exact; geen decimal-SUM in SQL vanwege SQLite).
    // - Nooit eenheden omrekenen: groeperen per variant + route (+ klant bij DirectTrade) + eenheid.
    // - Netto-kg alleen uit werkelijke inhoudsregels; verwacht-maar-niet-geregistreerd heeft nooit kg.
    // - Verwacht = open bronregel minus geregistreerde inhoud in dezelfde eenheid (niet op Cancelled-pallets).
    // - Gesloten inhoud (Dispatched/Delivered/Cancelled) telt nergens mee; DirectTrade nooit als magazijnvoorraad.
    public static class AvailabilityCalculator
    {
        private sealed record GroupKey(int ProductVariantId, string Route, int? CustomerId, string Unit);

        private sealed class Group
        {
            public GroupKey Key { get; init; }
            public string ProductName { get; set; }
            public string VariantName { get; set; }
            public string CustomerName { get; set; }
            public QualityBreakdownDto Warehouse { get; } = new();
            public QualityBreakdownDto ReceivedOutsideWarehouse { get; } = new();
            public QualityBreakdownDto InTransit { get; } = new();
            public QualityBreakdownDto ExpectedOnPallets { get; } = new();
            public decimal Unregistered { get; set; }
            public List<AvailabilityWarningDto> Warnings { get; } = new();

            public QualityBreakdownDto For(string category) => category switch
            {
                AvailabilityRules.CategoryWarehouse => Warehouse,
                AvailabilityRules.CategoryReceivedOutsideWarehouse => ReceivedOutsideWarehouse,
                AvailabilityRules.CategoryInTransit => InTransit,
                AvailabilityRules.CategoryExpectedOnPallets => ExpectedOnPallets,
                _ => throw new InvalidOperationException($"Category {category} has no bucket.")
            };
        }

        public static List<VariantAvailabilityDto> BuildOverview(
            IReadOnlyCollection<SourceLineFact> sources,
            IReadOnlyCollection<ContentLineFact> contents,
            AvailabilityQuery query)
        {
            var registered = RegisteredBySource(sources, contents);
            var groups = new Dictionary<GroupKey, Group>();

            Group GroupFor(int variantId, string productName, string variantName, string route, int? customerId, string customerName, string unit)
            {
                var key = new GroupKey(variantId, route, route == SupplyRules.RouteDirectTrade ? customerId : null, unit);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Group { Key = key, ProductName = productName, VariantName = variantName, CustomerName = customerName };
                    groups[key] = group;
                }
                return group;
            }

            foreach (var content in contents)
            {
                var category = AvailabilityRules.Classify(content.LogisticsStatus, content.LocationContext);
                if (category == null || category == AvailabilityRules.CategoryClosed)
                {
                    continue;
                }

                var group = GroupFor(content.ProductVariantId, content.ProductName, content.VariantName, content.Route, content.CustomerId, content.CustomerName, content.Unit);

                // Kan via de API niet ontstaan (guard-regel 4); legacy/SQL-data mag de magazijnvoorraad nooit ophogen.
                if (content.Route == SupplyRules.RouteDirectTrade && category == AvailabilityRules.CategoryWarehouse)
                {
                    group.Warnings.Add(new AvailabilityWarningDto
                    {
                        Code = AvailabilityRules.WarningDirectTradeInWarehouse,
                        SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                        PalletContentLineId = content.PalletContentLineId,
                        Quantity = content.Quantity
                    });
                    continue;
                }

                Add(group.For(category), content.QualityStatus, content.Quantity, content.NetWeightKg);
            }

            foreach (var source in sources.Where(s => AvailabilityRules.IsOpenCommitmentStatus(s.CommitmentStatus)))
            {
                var group = GroupFor(source.ProductVariantId, source.ProductName, source.VariantName, source.Route, source.CustomerId, source.CustomerName, source.Unit);
                var (unregistered, warning) = Unregistered(source, registered);
                group.Unregistered += unregistered;
                if (warning != null)
                {
                    group.Warnings.Add(warning);
                }
            }

            return groups.Values
                .Where(g => Matches(g.Key.ProductVariantId, g.Key.Route, g.Key.CustomerId, g.Key.Unit, query))
                .GroupBy(g => g.Key.ProductVariantId)
                .Select(variant =>
                {
                    var first = variant.First();
                    return new VariantAvailabilityDto
                    {
                        ProductVariantId = variant.Key,
                        ProductName = first.ProductName,
                        VariantName = first.VariantName,
                        WarehouseSupply = OrderByUnit(variant.Where(g => g.Key.Route == SupplyRules.RouteWarehouse))
                            .Select(ToWarehouseSupply)
                            .ToList(),
                        DirectTradeSupply = variant.Where(g => g.Key.Route == SupplyRules.RouteDirectTrade)
                            .OrderBy(g => g.CustomerName, StringComparer.Ordinal)
                            .ThenBy(g => g.Key.CustomerId)
                            .ThenBy(g => AvailabilityRules.UnitOrder(g.Key.Unit))
                            .ThenBy(g => g.Key.Unit, StringComparer.Ordinal)
                            .Select(ToDirectTradeSupply)
                            .ToList()
                    };
                })
                .OrderBy(v => v.ProductName, StringComparer.Ordinal)
                .ThenBy(v => v.VariantName, StringComparer.Ordinal)
                .ThenBy(v => v.ProductVariantId)
                .ToList();
        }

        public static (List<SourceLineAvailabilityDto> SourceLines, List<ContentLineAvailabilityDto> ContentLines) BuildDrillDown(
            IReadOnlyCollection<SourceLineFact> sources,
            IReadOnlyCollection<ContentLineFact> contents,
            AvailabilityQuery query)
        {
            // Registratie telt altijd alle inhoud van de bron, los van weergavefilters.
            var registered = RegisteredBySource(sources, contents);

            var sourceLines = sources
                .Where(s => Matches(s.ProductVariantId, s.Route, s.CustomerId, s.Unit, query))
                .OrderBy(s => s.SupplyCommitmentLineId)
                .Select(s =>
                {
                    var open = AvailabilityRules.IsOpenCommitmentStatus(s.CommitmentStatus);
                    var (unregistered, warning) = open ? Unregistered(s, registered) : (0m, null);
                    return new SourceLineAvailabilityDto
                    {
                        SupplyCommitmentLineId = s.SupplyCommitmentLineId,
                        SupplyCommitmentId = s.SupplyCommitmentId,
                        Route = s.Route,
                        CustomerId = s.CustomerId,
                        CustomerName = s.CustomerName,
                        CommitmentStatus = s.CommitmentStatus,
                        ExpectedAt = s.ExpectedAt == null ? null : DateTime.SpecifyKind(s.ExpectedAt.Value, DateTimeKind.Utc),
                        Unit = s.Unit,
                        LotNumber = s.LotNumber,
                        ExpectedQuantity = s.ExpectedQuantity,
                        DeclaredExpectedNetWeightKg = s.DeclaredExpectedNetWeightKg,
                        RegisteredQuantity = s.Unit == null ? null : registered.GetValueOrDefault(s.SupplyCommitmentLineId),
                        UnregisteredQuantity = unregistered,
                        Warnings = warning == null ? new List<AvailabilityWarningDto>() : new List<AvailabilityWarningDto> { warning }
                    };
                })
                .ToList();

            var contentLines = contents
                .Select(c => (Fact: c, Category: AvailabilityRules.Classify(c.LogisticsStatus, c.LocationContext)))
                .Where(c => c.Category != null)
                .Where(c => c.Category != AvailabilityRules.CategoryClosed || query.IncludeClosed)
                .Where(c => Matches(c.Fact.ProductVariantId, c.Fact.Route, c.Fact.CustomerId, c.Fact.Unit, query))
                .Where(c => query.Lot == null || string.Equals(c.Fact.LotNumber, query.Lot, StringComparison.Ordinal))
                .Where(c => query.Category == null || c.Category == query.Category)
                .Where(c => query.QualityStatus == null || c.Fact.QualityStatus == query.QualityStatus)
                .OrderBy(c => c.Fact.PalletContentLineId)
                .Select(c => new ContentLineAvailabilityDto
                {
                    PalletContentLineId = c.Fact.PalletContentLineId,
                    PalletId = c.Fact.PalletId,
                    PalletReference = c.Fact.PalletReference,
                    Category = c.Category,
                    LogisticsStatus = c.Fact.LogisticsStatus,
                    LocationContext = c.Fact.LocationContext,
                    WarehouseLocation = c.Fact.WarehouseLocation,
                    Route = c.Fact.Route,
                    CustomerId = c.Fact.CustomerId,
                    CustomerName = c.Fact.CustomerName,
                    SupplyCommitmentLineId = c.Fact.SupplyCommitmentLineId,
                    LotNumber = c.Fact.LotNumber,
                    Unit = c.Fact.Unit,
                    Quantity = c.Fact.Quantity,
                    NetWeightKg = c.Fact.NetWeightKg,
                    QualityStatus = c.Fact.QualityStatus,
                    SellableNow = AvailabilityRules.IsAvailableNow(c.Category, c.Fact.QualityStatus, c.Fact.Route),
                    Closed = c.Category == AvailabilityRules.CategoryClosed
                })
                .ToList();

            return (sourceLines, contentLines);
        }

        // Geregistreerd per bronregel: inhoud in dezelfde eenheid als de bron, niet op een geannuleerde pallet.
        // Dispatched/Delivered telt wel: die goederen zijn echt aangekomen.
        private static Dictionary<int, decimal> RegisteredBySource(IReadOnlyCollection<SourceLineFact> sources, IReadOnlyCollection<ContentLineFact> contents)
        {
            var sourceUnits = sources.ToDictionary(s => s.SupplyCommitmentLineId, s => s.Unit);
            var registered = sources.ToDictionary(s => s.SupplyCommitmentLineId, _ => 0m);

            foreach (var content in contents.Where(c => c.LogisticsStatus != PalletRules.StatusCancelled))
            {
                if (sourceUnits.TryGetValue(content.SupplyCommitmentLineId, out var unit)
                    && unit != null
                    && string.Equals(unit, content.Unit, StringComparison.Ordinal))
                {
                    registered[content.SupplyCommitmentLineId] += content.Quantity;
                }
            }

            return registered;
        }

        private static (decimal Unregistered, AvailabilityWarningDto Warning) Unregistered(SourceLineFact source, IReadOnlyDictionary<int, decimal> registered)
        {
            if (source.Unit == null)
            {
                // Legacy-bron zonder eenheid: niets betrouwbaar af te trekken, dus volledig tonen met waarschuwing.
                return (source.ExpectedQuantity, new AvailabilityWarningDto
                {
                    Code = AvailabilityRules.WarningSourceUnitUnknown,
                    SupplyCommitmentLineId = source.SupplyCommitmentLineId
                });
            }

            var done = registered.GetValueOrDefault(source.SupplyCommitmentLineId);
            if (done > source.ExpectedQuantity)
            {
                return (0m, new AvailabilityWarningDto
                {
                    Code = AvailabilityRules.WarningOverRegistered,
                    SupplyCommitmentLineId = source.SupplyCommitmentLineId,
                    Quantity = done - source.ExpectedQuantity
                });
            }

            return (source.ExpectedQuantity - done, null);
        }

        // Klantfilter beperkt alleen DirectTrade: magazijnvoorraad is voor elke klant zichtbaar.
        private static bool Matches(int variantId, string route, int? customerId, string unit, AvailabilityQuery query) =>
            (query.ProductVariantId == null || variantId == query.ProductVariantId)
            && (query.Route == null || route == query.Route)
            && (query.Unit == null || unit == query.Unit)
            && (query.CustomerId == null || route != SupplyRules.RouteDirectTrade || customerId == query.CustomerId);

        private static void Add(QualityBreakdownDto breakdown, string qualityStatus, decimal quantity, decimal netWeightKg)
        {
            var bucket = qualityStatus switch
            {
                PalletRules.QualityApproved => breakdown.Approved,
                PalletRules.QualityQuarantine => breakdown.Quarantine,
                PalletRules.QualityBlocked => breakdown.Blocked,
                PalletRules.QualityRejected => breakdown.Rejected,
                _ => throw new InvalidOperationException($"Unknown quality status {qualityStatus}.")
            };
            bucket.Quantity += quantity;
            bucket.NetWeightKg += netWeightKg;
        }

        private static IEnumerable<Group> OrderByUnit(IEnumerable<Group> groups) =>
            groups
                .OrderBy(g => AvailabilityRules.UnitOrder(g.Key.Unit))
                .ThenBy(g => g.Key.Unit == null)
                .ThenBy(g => g.Key.Unit, StringComparer.Ordinal);

        private static WarehouseSupplyDto ToWarehouseSupply(Group group) => new()
        {
            Unit = group.Key.Unit,
            AvailableBeforeReservations = new AvailabilityQuantityDto
            {
                Quantity = group.Warehouse.Approved.Quantity,
                NetWeightKg = group.Warehouse.Approved.NetWeightKg
            },
            Warehouse = group.Warehouse,
            ReceivedOutsideWarehouse = group.ReceivedOutsideWarehouse,
            InTransit = group.InTransit,
            ExpectedOnPallets = group.ExpectedOnPallets,
            ExpectedUnregistered = new ExpectedUnregisteredDto { Quantity = group.Unregistered },
            Warnings = group.Warnings
        };

        private static DirectTradeSupplyDto ToDirectTradeSupply(Group group) => new()
        {
            CustomerId = group.Key.CustomerId,
            CustomerName = group.CustomerName,
            Unit = group.Key.Unit,
            ReceivedOutsideWarehouse = group.ReceivedOutsideWarehouse,
            InTransit = group.InTransit,
            ExpectedOnPallets = group.ExpectedOnPallets,
            ExpectedUnregistered = new ExpectedUnregisteredDto { Quantity = group.Unregistered },
            Warnings = group.Warnings
        };
    }
}
