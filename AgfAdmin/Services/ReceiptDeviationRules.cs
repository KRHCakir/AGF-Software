namespace AgfAdmin.Services
{
    // Afwijkende ontvangst binnen een inkooporder (SupplyReceiptDeviation): vergelijken van varianten en het verlagen van de
    // oorspronkelijke regel. Gespiegeld in agf-frontend/src/lib/receiptDeviation.ts (kenmerken en teksten in het venster).
    //
    // Een product is altijd een vaste sleutel (ProductVariantId). Alleen dezelfde variant boekt zonder vragen; elk verschil vraagt een
    // bevestiging. Een ander artikel (ander product, of hetzelfde product met een ander ras/naam zoals Hass -> Fuerte) is nooit een
    // afwijking: dat is een nieuwe aanvoerregel. Kenmerken worden exact vergeleken (alleen spaties aan de randen en leeg = null tellen
    // niet), nooit "bijna gelijk".
    public static class ReceiptDeviationRules
    {
        public const string KindOrigin = "Origin";
        public const string KindSize = "Size";
        public const string KindPackaging = "Packaging";
        public const string KindTradeClass = "TradeClass";
        public const string KindBrand = "Brand";
        // Alle kenmerken gelijk, toch een andere variant (bijv. twee bijna gelijke artikelen): nooit stilzwijgend gelijkgesteld.
        public const string KindOther = "Other";
        // Netto kg per verpakking wijkt meer dan WeightTolerance af van wat de bronregel verwachtte (ook bij dezelfde variant).
        public const string KindWeight = "Weight";
        public static readonly IReadOnlyList<string> Kinds = new[] { KindOrigin, KindSize, KindPackaging, KindTradeClass, KindBrand, KindOther, KindWeight };

        // Tolerantie voor het netto kg per verpakking: meer dan 2% verschil (relatief aan de verwachting) is een afwijking.
        // Precies 2% niet. Een deellevering of meer colli dan besteld is nooit een gewichtsafwijking: er wordt per verpakking vergeleken.
        public const decimal WeightTolerance = 0.02m;

        public const string ReasonSupplierDeliveredDifferently = "SupplierDeliveredDifferently";
        public const string ReasonOtherLotLoaded = "OtherLotLoaded";
        public const string ReasonOther = "Other";
        public static readonly IReadOnlyList<string> Reasons = new[] { ReasonSupplierDeliveredDifferently, ReasonOtherLotLoaded, ReasonOther };

        // Codes in het 409-antwoord; de client toont daarop het venster of een melding in gewone taal.
        public const string CodeConfirmationRequired = "DEVIATION_CONFIRMATION_REQUIRED";
        public const string CodeBlockedAssigned = "DEVIATION_BLOCKED_ASSIGNED";
        public const string CodeOtherArticle = "DEVIATION_OTHER_ARTICLE";

        public static bool IsReason(string value) => value != null && Reasons.Contains(value, StringComparer.Ordinal);

        // De kenmerken die een variant onderscheiden. Maat staat in SizeCategory ("12", "47+"); handelsklasse in TradeClass;
        // verpakking is de omschrijving (Packaging, "4kg Box") plus de verpakkingseenheid (PackagingUnit, colli/box).
        // Herkomst: met een land aan beide kanten telt alleen de landcode ("PE" en "Peru" zijn hetzelfde land); anders de tekst.
        public sealed record VariantFacts(int Id, int ProductId, string ProductName, string Name, string Origin, string Size,
            string Packaging, string PackagingUnit, string TradeClass, string Brand, string OriginCountryCode = null);

        private static bool SameOrigin(VariantFacts a, VariantFacts b) =>
            a.OriginCountryCode != null && b.OriginCountryCode != null
                ? string.Equals(a.OriginCountryCode, b.OriginCountryCode, StringComparison.Ordinal)
                : Same(a.Origin, b.Origin);

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        private static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

        // Hetzelfde artikel: zelfde product en zelfde ras/naam. Alleen dan kan een andere variant een afwijking zijn.
        public static bool IsSameArticle(VariantFacts expected, VariantFacts received) =>
            expected.ProductId == received.ProductId && Same(expected.Name, received.Name);

        // Afwijkende kenmerken in vaste volgorde; leeg alleen bij dezelfde variant.
        public static IReadOnlyList<string> DifferingKinds(VariantFacts expected, VariantFacts received)
        {
            if (expected.Id == received.Id)
            {
                return Array.Empty<string>();
            }
            var kinds = new List<string>();
            if (!SameOrigin(expected, received)) kinds.Add(KindOrigin);
            if (!Same(expected.Size, received.Size)) kinds.Add(KindSize);
            if (!Same(expected.Packaging, received.Packaging) || !Same(expected.PackagingUnit, received.PackagingUnit)) kinds.Add(KindPackaging);
            if (!Same(expected.TradeClass, received.TradeClass)) kinds.Add(KindTradeClass);
            if (!Same(expected.Brand, received.Brand)) kinds.Add(KindBrand);
            if (kinds.Count == 0) kinds.Add(KindOther);
            return kinds;
        }

        // Verwacht netto kg per verpakking: het verwachte kg van de bronregel gedeeld door haar aantal, anders de artikelstandaard
        // van de bestelde variant; null als er geen verwachting is (dan geen gewichtscontrole). Bruto telt hier nooit mee.
        public static decimal? ExpectedKgPerPackage(decimal expectedQuantity, decimal? expectedNetKg, decimal? defaultNetKgPerPackage)
        {
            if (expectedNetKg is > 0m && expectedQuantity > 0m) return expectedNetKg.Value / expectedQuantity;
            return defaultNetKgPerPackage is > 0m ? defaultNetKgPerPackage : null;
        }

        // Ontvangen kg per verpakking: het netto totaal (gewogen of etiket × aantal) gedeeld door het aantal.
        public static decimal ReceivedKgPerPackage(decimal quantity, decimal netKg) => quantity > 0m ? netKg / quantity : 0m;

        public static bool IsWeightDeviation(decimal? expectedKgPerPackage, decimal quantity, decimal receivedNetKg)
        {
            if (expectedKgPerPackage is not > 0m || quantity <= 0m) return false;
            var received = ReceivedKgPerPackage(quantity, receivedNetKg);
            return Math.Abs(received - expectedKgPerPackage.Value) / expectedKgPerPackage.Value > WeightTolerance;
        }

        // Na "Ja" bij een gewichtsafwijking: de hele regel verwacht voortaan het ontvangen kg per verpakking (opgegeven, 3 decimalen).
        // Aantal en palletweergave blijven; bruto blijft alleen staan als het niet onder het nieuwe netto komt (nooit bruto verzinnen).
        public static LineShape Reweigh(LineShape line, decimal receivedKgPerPackage)
        {
            if (line.ExpectedQuantity <= 0m) return line;
            var net = decimal.Round(line.ExpectedQuantity * receivedKgPerPackage, 3, MidpointRounding.AwayFromZero);
            if (net <= 0m) return line;
            var gross = line.GrossKg is { } kg && kg >= net ? line.GrossKg : null;
            return line with { NetKg = net, GrossKg = gross, WeightSource = LineWeightRules.Entered };
        }

        // Hoeveel de oorspronkelijke regel omlaag gaat: de ontvangen hoeveelheid, maar nooit onder wat er al op die regel is ontvangen.
        // Het deel daarboven is meer geleverd dan besteld (de vervangende regel verwacht wel de hele ontvangen hoeveelheid).
        public static decimal ReducedQuantity(decimal expectedQuantity, decimal receivedQuantity, decimal quantity) =>
            Math.Min(quantity, Math.Max(0m, expectedQuantity - receivedQuantity));

        // Wat er van een aanvoerregel verandert als hij omlaag gaat.
        public sealed record LineShape(decimal ExpectedQuantity, decimal? NetKg, decimal? GrossKg, string WeightSource,
            decimal? PalletCount, decimal? PackagesPerPallet, decimal? LoosePackages);

        // Verlaagt de regel met `reduced` en houdt kg en palletweergave consistent:
        // - kg (netto en bruto) gaan evenredig mee (zelfde kg per verpakking, 3 decimalen, half van nul af); de bron blijft gelijk.
        //   Bij een berekende regel is dat exact aantal × dezelfde standaard; bij opgegeven kg blijft de verwachting per verpakking gelijk.
        //   Verwacht 0: geen kg meer (leeg, zonder bron).
        // - palletweergave: met hele pallets en verpakkingen per pallet opnieuw verdeeld ("3 × 120 + 5"); anders (gebroken waarden of
        //   minder dan één pallet) leeg, zodat er nooit een weergave staat die niet bij het aantal past.
        public static LineShape Reduce(LineShape line, decimal reduced)
        {
            if (reduced <= 0m)
            {
                return line;
            }
            var after = line.ExpectedQuantity - reduced;
            decimal? Scale(decimal? kg)
            {
                if (kg == null || after <= 0m || line.ExpectedQuantity <= 0m) return null;
                var scaled = decimal.Round(kg.Value * after / line.ExpectedQuantity, 3, MidpointRounding.AwayFromZero);
                return scaled > 0m ? scaled : null;
            }
            var net = Scale(line.NetKg);
            var gross = Scale(line.GrossKg);
            var source = net == null && gross == null ? null : line.WeightSource;

            decimal? palletCount = null, perPallet = null, loose = null;
            if (line.PalletCount != null && line.PackagesPerPallet != null
                && PackagingRules.IsWholeNumber(line.PackagesPerPallet.Value) && line.PackagesPerPallet.Value > 0m
                && PackagingRules.IsWholeNumber(after))
            {
                var count = decimal.Floor(after / line.PackagesPerPallet.Value);
                if (count > 0m)
                {
                    var rest = after - count * line.PackagesPerPallet.Value;
                    palletCount = count;
                    perPallet = line.PackagesPerPallet;
                    loose = rest == 0m && line.LoosePackages == null ? null : rest;
                }
            }
            return new LineShape(after, net, gross, source, palletCount, perPallet, loose);
        }
    }
}
