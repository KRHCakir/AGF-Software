using AgfAdmin.Models;
namespace AgfAdmin.Services
{
    // Centrale controle van de toestand die na een wijziging ontstaat, gebruikt door elk pallet-schrijfpad
    // (inhoud toevoegen, logistieke status, locatie). De regels binnen één rij staan ook als database-check;
    // de regel "geen DirectTrade in het magazijn" staat daarnaast in de TraceabilityGuard.
    public static class PalletInvariants
    {
        public sealed record ContentSource(string Route, int? CustomerId);

        public static string CheckStateCombination(string logisticsStatus, string locationContext, string warehouseLocation)
        {
            var isWarehouse = locationContext == PalletRules.LocationWarehouse;
            var isReceived = logisticsStatus == PalletRules.StatusReceived;

            if (isWarehouse && !isReceived)
            {
                return $"A pallet can only be in the warehouse when it is {PalletRules.StatusReceived}, not {logisticsStatus}.";
            }
            if (warehouseLocation != null && !(isWarehouse && isReceived))
            {
                return "A warehouse location is only allowed for a received pallet in the warehouse.";
            }
            if (logisticsStatus == PalletRules.StatusDelivered && !PalletRules.DeliveredLocations.Contains(locationContext, StringComparer.Ordinal))
            {
                return $"A delivered pallet must be at one of: {string.Join(", ", PalletRules.DeliveredLocations)}.";
            }

            return null;
        }

        public static string CheckContent(string locationContext, IReadOnlyCollection<ContentSource> contentSources)
        {
            if (locationContext == PalletRules.LocationWarehouse && contentSources.Any(s => s.Route == SupplyRules.RouteDirectTrade))
            {
                return "DirectTrade supply can never be on a warehouse pallet.";
            }
            if (contentSources.Select(s => s.Route).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return "Warehouse and DirectTrade content cannot be mixed on one pallet.";
            }
            if (contentSources.Where(s => s.Route == SupplyRules.RouteDirectTrade).Select(s => s.CustomerId).Distinct().Count() > 1)
            {
                return "DirectTrade content for different customers cannot be mixed on one pallet.";
            }

            return null;
        }
    
        // Fysieke gewichtsgrens: tara (indien gemeten) plus de voorraad-kg van alle inhoud (ontvangst minus afgeboekt) plus het
        // toe te voegen kg past binnen het gemeten bruto. Historische ontvangsten worden hier bewust niet opgeteld:
        // verzonden kg is niet meer op de pallet, en teruggeboekte inhoud zou anders dubbel wegen.
        public static string CheckGrossWeight(decimal? grossKg, decimal? tareKg, IEnumerable<PalletContentLine> contentLines, decimal addedNetWeightKg)
        {
            if (grossKg == null)
            {
                return null;
            }
            var onHand = contentLines.Sum(c => ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg, c.SplitOffNetWeightKg));
            var totalKg = (tareKg ?? 0m) + onHand + addedNetWeightKg;
            return totalKg > grossKg.Value
                ? $"Tare plus on-hand content net weight ({totalKg} kg) would exceed the measured gross weight ({grossKg} kg)."
                : null;
        }
}
}
