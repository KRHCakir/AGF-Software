namespace AgfAdmin.Services
{
    // Afkeuren van ontvangen inkoop: redenen en de standaard-kg van een deelafkeuring (zie PurchaseOrderRules.cs).
    public static partial class PurchaseOrderRules
    {
        public const int MaxRejectionLines = 200;

        public const string ReasonOther = "Other";
        public static readonly IReadOnlyList<string> RejectionReasons = new[] { "Quality", "Ripeness", "Temperature", "Damaged", "WrongArticle", ReasonOther };

        public static bool IsRejectionReason(string value) => value != null && RejectionReasons.Contains(value, StringComparer.Ordinal);
        public static bool IsRejectionDecision(string value) => value != null && RejectionDecisions.Contains(value, StringComparer.Ordinal);

        // Standaard-kg bij een deelafkeuring: berekend gewicht = aantal × kg per verpakking; gewogen = evenredig, op 3 decimalen;
        // de hele resterende voorraad krijgt altijd exact de resterende kg (zelfde regel als bij verzenden).
        public static decimal DefaultRejectedKg(decimal quantity, decimal onHandQuantity, decimal onHandNetWeightKg, string weightMethod, decimal? netKgPerPackage)
        {
            if (quantity >= onHandQuantity) return onHandNetWeightKg;
            if (weightMethod == WeightRules.Calculated && netKgPerPackage != null) return WeightRules.CalculatedTotal(quantity, netKgPerPackage.Value);
            return onHandQuantity <= 0m ? 0m : decimal.Round(onHandNetWeightKg * quantity / onHandQuantity, 3, MidpointRounding.AwayFromZero);
        }
    }
}
