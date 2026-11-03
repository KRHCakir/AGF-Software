namespace AgfAdmin.Services
{
    // Open verkooporderregel als plat feit (alleen orders met status Open). Reserveringen opgeteld per soort, in de eenheid van de regel.
    public sealed record SalesLineFact(
        int SalesOrderLineId,
        int SalesOrderId,
        string OrderNumber,
        int CustomerId,
        string CustomerName,
        string CustomerReference,
        DateTime OrderCreatedAt,
        DateTime RequestedDeliveryDate,
        int ProductVariantId,
        string ProductName,
        string VariantName,
        string Unit,
        string SupplySource,
        decimal Quantity,
        // Active Physical op inhoud die nu Approved is; op inhoud die dat niet meer is (on hold); Active DirectTrade; Consumed (alle bronnen).
        decimal ReservedApproved = 0m,
        decimal ReservedOnHold = 0m,
        decimal Allocated = 0m,
        decimal Consumed = 0m)
    {
        // Verkocht en nog niet geleverd: besteld minus verzonden, zonder wat uit klantgebonden aanvoer (DirectTrade) komt: dat telt
        // ook niet als Verwacht. Gereserveerd en niet gereserveerd allebei.
        public decimal OpenDemand => Math.Max(0m, Quantity - Consumed - Allocated);
        // Gereserveerd op goedgekeurde voorraad (zit al in Voorraad). Een reservering op geblokkeerde inhoud dekt de vraag niet.
        public decimal Reserved => Math.Min(ReservedApproved, OpenDemand);
        public decimal Unreserved => OpenDemand - Reserved;
    }

    // Een deel van een verwachte inkooporder dat een orderregel dekt (oudste verkoop eerst, vroegst verwachte aanvoer eerst).
    public sealed record ExpectedCover(int SupplyCommitmentId, string OrderNumber, DateTime? ExpectedAt, decimal Quantity, bool InInspection);

    // Dekking van het niet-gereserveerde deel van een orderregel: nu uit vrije voorraad te reserveren, uit verwachte aanvoer, of tekort.
    public sealed record LineCoverage(int SalesOrderLineId, decimal FromStock, IReadOnlyList<ExpectedCover> Expected, decimal Shortage)
    {
        public decimal FromExpected => Expected.Sum(e => e.Quantity);
    }

    // Verwachte aanvoer per inkooporderregel (alleen route Warehouse): nog niet ontvangen, en ontvangen maar nog in keuring.
    public sealed record ExpectedSupply(int SupplyCommitmentLineId, int SupplyCommitmentId, string OrderNumber, DateTime? ExpectedAt, string LotNumber, decimal NotReceived, decimal InInspection)
    {
        public decimal Total => NotReceived + InInspection;
    }

    // Stand per variant + eenheid. Vrij te verkopen = Voorraad + Verwacht - Verkocht en mag negatief zijn (tekort: waarschuwing, geen blokkade).
    // Vrij op voorraad = Voorraad - gereserveerd - niet gereserveerd verkocht uit voorraad: de grens voor een nieuwe regel "Uit voorraad".
    public sealed class SalesBalance
    {
        public int ProductVariantId { get; init; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public string Unit { get; init; }
        public decimal Stock { get; set; }
        public decimal StockReserved { get; set; }
        public decimal ExpectedNotReceived { get; set; }
        public decimal ExpectedInInspection { get; set; }
        public decimal Expected => ExpectedNotReceived + ExpectedInInspection;
        public decimal Sold { get; set; }
        public decimal SoldReserved { get; set; }
        public decimal SoldFromStockUnreserved { get; set; }
        public decimal Free => Stock + Expected - Sold;
        public decimal FreeStock => Stock - StockReserved - SoldFromStockUnreserved;
        public decimal ShortageQuantity => Math.Max(0m, -Free);
        public List<ExpectedSupply> Supplies { get; } = new();
        public List<SalesLineFact> Lines { get; } = new();
        public Dictionary<int, LineCoverage> Coverage { get; } = new();
    }

    // Pure berekening (exact decimal, in het geheugen) op dezelfde feiten als het beschikbaarheidsoverzicht. Nooit eenheden omrekenen,
    // altijd per exacte variant (ProductVariantId), nooit op tekst.
    // - Voorraad: goedgekeurde inhoud op een ontvangen pallet in het eigen magazijn, route Warehouse (quarantaine, geblokkeerd en afgekeurd niet).
    // - Verwacht: open inkoopregels van route Warehouse: nog niet ontvangen (verwacht min geregistreerd, plus inhoud op verwachte of onderweg
    //   zijnde pallets) en ontvangen maar nog in quarantaine (in keuring). Klantgebonden aanvoer (DirectTrade) telt nooit mee.
    // - Verkocht: open orderregels, besteld min verzonden min klantgebonden toegewezen; gereserveerd en niet gereserveerd.
    public static class SalesBalanceCalculator
    {
        public static Dictionary<(int ProductVariantId, string Unit), SalesBalance> Build(
            IReadOnlyCollection<SourceLineFact> sources,
            IReadOnlyCollection<ContentLineFact> contents,
            IReadOnlyCollection<SalesLineFact> lines)
        {
            var balances = new Dictionary<(int, string), SalesBalance>();
            SalesBalance For(int variantId, string productName, string variantName, string unit)
            {
                if (!balances.TryGetValue((variantId, unit), out var balance))
                {
                    balance = new SalesBalance { ProductVariantId = variantId, ProductName = productName, VariantName = variantName, Unit = unit };
                    balances[(variantId, unit)] = balance;
                }
                return balance;
            }

            var warehouseSources = sources.Where(s => s.Route == SupplyRules.RouteWarehouse && s.Unit != null).ToDictionary(s => s.SupplyCommitmentLineId);
            var registered = new Dictionary<int, decimal>();
            var onPallets = new Dictionary<int, decimal>();
            var inInspection = new Dictionary<int, decimal>();

            foreach (var content in contents.Where(c => c.Route == SupplyRules.RouteWarehouse))
            {
                // Registratie tegen de bronregel (zoals AvailabilityCalculator): dezelfde eenheid, niet op een geannuleerde pallet, geen correctie- of afgesplitste inhoud.
                if (warehouseSources.TryGetValue(content.SupplyCommitmentLineId, out var source) && content.LogisticsStatus != PalletRules.StatusCancelled
                    && !content.FromCorrection && !content.FromRejectionSplit && string.Equals(source.Unit, content.Unit, StringComparison.Ordinal))
                {
                    registered[source.SupplyCommitmentLineId] = registered.GetValueOrDefault(source.SupplyCommitmentLineId) + content.Quantity;
                }

                var category = AvailabilityRules.Classify(content.LogisticsStatus, content.LocationContext);
                var usable = content.QualityStatus is PalletRules.QualityApproved or PalletRules.QualityQuarantine;
                if (category == AvailabilityRules.CategoryWarehouse && content.QualityStatus == PalletRules.QualityApproved)
                {
                    var balance = For(content.ProductVariantId, content.ProductName, content.VariantName, content.Unit);
                    balance.Stock += content.OnHandQuantity;
                    balance.StockReserved += Math.Min(content.ReservedQuantity, content.OnHandQuantity);
                }
                else if (category == AvailabilityRules.CategoryWarehouse && content.QualityStatus == PalletRules.QualityQuarantine)
                {
                    inInspection[content.SupplyCommitmentLineId] = inInspection.GetValueOrDefault(content.SupplyCommitmentLineId) + content.OnHandQuantity;
                    For(content.ProductVariantId, content.ProductName, content.VariantName, content.Unit).ExpectedInInspection += content.OnHandQuantity;
                }
                else if (usable && category is AvailabilityRules.CategoryExpectedOnPallets or AvailabilityRules.CategoryInTransit)
                {
                    onPallets[content.SupplyCommitmentLineId] = onPallets.GetValueOrDefault(content.SupplyCommitmentLineId) + content.OnHandQuantity;
                    For(content.ProductVariantId, content.ProductName, content.VariantName, content.Unit).ExpectedNotReceived += content.OnHandQuantity;
                }
            }

            foreach (var source in warehouseSources.Values.OrderBy(s => s.SupplyCommitmentLineId))
            {
                var unregistered = AvailabilityRules.IsOpenCommitmentStatus(source.CommitmentStatus)
                    ? Math.Max(0m, source.ExpectedQuantity - registered.GetValueOrDefault(source.SupplyCommitmentLineId))
                    : 0m;
                var notReceived = unregistered + onPallets.GetValueOrDefault(source.SupplyCommitmentLineId);
                var inspection = inInspection.GetValueOrDefault(source.SupplyCommitmentLineId);
                if (unregistered > 0m)
                {
                    For(source.ProductVariantId, source.ProductName, source.VariantName, source.Unit).ExpectedNotReceived += unregistered;
                }
                if (notReceived > 0m || inspection > 0m)
                {
                    For(source.ProductVariantId, source.ProductName, source.VariantName, source.Unit).Supplies.Add(new ExpectedSupply(
                        source.SupplyCommitmentLineId, source.SupplyCommitmentId, source.SupplyOrderNumber, source.ExpectedAt, source.LotNumber, notReceived, inspection));
                }
            }

            foreach (var line in lines)
            {
                var balance = For(line.ProductVariantId, line.ProductName, line.VariantName, line.Unit);
                balance.Lines.Add(line);
                balance.Sold += line.OpenDemand;
                balance.SoldReserved += line.Reserved;
                if (line.SupplySource == SalesSupplyRules.SourceStock)
                {
                    balance.SoldFromStockUnreserved += line.Unreserved;
                }
            }

            foreach (var balance in balances.Values)
            {
                Cover(balance);
            }
            return balances;
        }

        // Oudste verkoop eerst: eerst de regels uit voorraad (die zijn uit voorraad beloofd), dan die uit verwachte aanvoer, elk op
        // aanmaakmoment van de order en dan regelnummer. Elke regel neemt eerst vrije voorraad, dan aanvoer in keuring, dan de vroegst
        // verwachte inkooporder (zonder datum als laatste); wat overblijft is tekort. Som van de tekorten = max(0, -Vrij).
        private static void Cover(SalesBalance balance)
        {
            var stockPool = Math.Max(0m, balance.Stock - balance.StockReserved);
            var supplies = balance.Supplies
                .SelectMany(s => new[]
                {
                    (Supply: s, InInspection: true, Left: s.InInspection),
                    (Supply: s, InInspection: false, Left: s.NotReceived)
                })
                .Where(s => s.Left > 0m)
                .OrderBy(s => s.InInspection ? 0 : 1)
                .ThenBy(s => s.Supply.ExpectedAt == null)
                .ThenBy(s => s.Supply.ExpectedAt)
                .ThenBy(s => s.Supply.SupplyCommitmentId)
                .ThenBy(s => s.Supply.SupplyCommitmentLineId)
                .Select(s => new SupplyLeft(s.Supply, s.InInspection, s.Left))
                .ToList();

            foreach (var line in balance.Lines
                .OrderBy(l => l.SupplySource == SalesSupplyRules.SourceStock ? 0 : 1)
                .ThenBy(l => l.OrderCreatedAt)
                .ThenBy(l => l.SalesOrderLineId))
            {
                var need = line.Unreserved;
                var fromStock = Math.Min(need, stockPool);
                stockPool -= fromStock;
                need -= fromStock;
                var expected = new List<ExpectedCover>();
                foreach (var supply in supplies)
                {
                    if (need <= 0m) break;
                    if (supply.Left <= 0m) continue;
                    var take = Math.Min(need, supply.Left);
                    supply.Left -= take;
                    need -= take;
                    var last = expected.Count > 0 ? expected[^1] : null;
                    if (last != null && last.SupplyCommitmentId == supply.Supply.SupplyCommitmentId && last.InInspection == supply.InInspection)
                    {
                        expected[^1] = last with { Quantity = last.Quantity + take };
                    }
                    else
                    {
                        expected.Add(new ExpectedCover(supply.Supply.SupplyCommitmentId, supply.Supply.OrderNumber, supply.Supply.ExpectedAt, take, supply.InInspection));
                    }
                }
                balance.Coverage[line.SalesOrderLineId] = new LineCoverage(line.SalesOrderLineId, fromStock, expected, need);
            }
        }

        private sealed class SupplyLeft
        {
            public SupplyLeft(ExpectedSupply supply, bool inInspection, decimal left) { Supply = supply; InInspection = inInspection; Left = left; }
            public ExpectedSupply Supply { get; }
            public bool InInspection { get; }
            public decimal Left { get; set; }
        }
    }
}
