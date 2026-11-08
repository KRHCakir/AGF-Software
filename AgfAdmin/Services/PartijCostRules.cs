namespace AgfAdmin.Services
{
    // Regels voor kosten op partijen (P2). Gespiegeld in agf-frontend/src/lib/costAllocation.ts (verdelen en afronden).
    public static class PartijCostRules
    {
        public const string EventAllocated = "Allocated";
        public const string EventChanged = "Changed";
        public const string EventRemoved = "Removed";
        public static readonly IReadOnlyList<string> EventTypes = new[] { EventAllocated, EventChanged, EventRemoved };

        // Soort van een regel met een artikel (goederen via een inkooporder) of zonder kostenartikel en artikel.
        public const string KindGoods = "Goederen";
        public const string KindOther = CostArticleRules.Other;
        public const int MaxKindLength = 100;
        public const int MaxActorLength = 100;
        public const int MaxAllocationsPerLine = 100;
        // Kosten op partijen staan altijd in euro; een factuur in een andere valuta wordt omgerekend met haar ECB-koers (ExchangeRateRules).
        public const string Currency = ExchangeRateRules.Euro;

        public static string KindOf(int? productVariantId, string costArticleName) =>
            costArticleName ?? (productVariantId != null ? KindGoods : KindOther);

        // Positief, hoogstens twee decimalen en binnen decimal(18,2).
        public static bool IsAmount(decimal value) => value > 0m && decimal.Round(value, 2) == value && value <= 9_999_999_999_999_999.99m;
    }
}
