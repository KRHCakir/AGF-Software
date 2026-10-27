namespace AgfAdmin.Services
{
    // Netto en bruto kg op een verkooporderregel of aanvoerregel ("1.560 colli × 10 kg = 15.600,0 kg netto · 16.068,0 kg bruto").
    // Beide optioneel (het gewogen gewicht volgt pas bij levering), > 0, hooguit 3 decimalen, en bruto nooit onder netto.
    // WeightSource geldt voor de hele regel:
    // - null: de regel heeft geen kg (beide leeg).
    // - Calculated: elk ingevuld kg-veld is exact aantal × de standaard kg per verpakking van het artikel (3 decimalen, half
    //   van nul af, zoals WeightRules.CalculatedTotal). De server controleert dat bij aanmaken tegen de huidige artikelstandaard.
    // - Entered: minstens één kg-veld is door de gebruiker ingevuld of overschreven (werkelijk opgegeven, nooit afgeleid);
    //   het andere veld mag dan nog de berekende waarde hebben. Er wordt niets teruggerekend. Kg zonder bron telt als Entered.
    // Het artikel wordt hierdoor nooit aangepast, en een regel volgt een latere wijziging van de artikelstandaard niet.
    public static class LineWeightRules
    {
        public const string Calculated = "Calculated";
        public const string Entered = "Entered";
        public static readonly string[] Sources = { Calculated, Entered };

        public static bool IsWeightSource(string value) => value == Calculated || value == Entered;

        // Vorm van de invoer (zonder artikel): kg positief met hooguit 3 decimalen, bruto >= netto, en een bron alleen als er kg is.
        // Kg zonder bron is Entered: zo bedoelde een oudere aanvoeropdracht met alleen netto kg het ook, en die blijft herkend.
        // Geeft de (genormaliseerde) bron terug.
        public static string Validate(decimal? netKg, decimal? grossKg, string source, string key, string netField, string grossField,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            var ok = true;
            if (netKg != null && !SupplyRules.IsPositiveDecimal18_3(netKg.Value))
            {
                addError(errors, $"{key}.{netField}", "Net weight (kg), when given, must be positive and may have at most 3 decimals.");
                ok = false;
            }
            if (grossKg != null && !SupplyRules.IsPositiveDecimal18_3(grossKg.Value))
            {
                addError(errors, $"{key}.{grossField}", "Gross weight (kg), when given, must be positive and may have at most 3 decimals.");
                ok = false;
            }
            if (ok && netKg != null && grossKg != null && grossKg.Value < netKg.Value)
            {
                addError(errors, $"{key}.{grossField}", "Gross weight (kg) may not be less than net weight (kg).");
            }
            var anyKg = netKg != null || grossKg != null;
            if (!anyKg)
            {
                if (source != null)
                {
                    addError(errors, $"{key}.weightSource", "Weight source is only allowed together with net or gross kg.");
                }
                return null;
            }
            if (source == null)
            {
                return Entered;
            }
            if (!IsWeightSource(source))
            {
                addError(errors, $"{key}.weightSource", $"Weight source must be one of: {string.Join(", ", Sources)}, or be omitted (= Entered).");
            }
            return source;
        }

        // Alleen voor nieuwe invoer met bron Calculated (na de replaycontrole, zodat een eerder opgeslagen opdracht herkend blijft,
        // ook als de artikelstandaard intussen is gewijzigd): elk ingevuld veld moet exact aantal × artikelstandaard zijn.
        public static void CalculatedErrors(decimal quantity, decimal? netKg, decimal? grossKg, string source,
            decimal? defaultNetKgPerPackage, decimal? defaultGrossKgPerPackage, string key, string netField, string grossField,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            if (source != Calculated)
            {
                return;
            }
            CheckCalculated(quantity, netKg, defaultNetKgPerPackage, $"{key}.{netField}", "net", errors, addError);
            CheckCalculated(quantity, grossKg, defaultGrossKgPerPackage, $"{key}.{grossField}", "gross", errors, addError);
        }

        private static void CheckCalculated(decimal quantity, decimal? kg, decimal? perPackage, string field, string kind,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            if (kg == null)
            {
                return;
            }
            if (perPackage == null)
            {
                addError(errors, field, $"The article has no default {kind} kg per package; send weightSource Entered for kg you entered yourself.");
                return;
            }
            if (!WeightRules.TryCalculatedTotal(quantity, perPackage.Value, out var expected) || expected != kg.Value)
            {
                addError(errors, field, $"Calculated {kind} kg must equal quantity × the article's default {kind} kg per package ({quantity} × {perPackage.Value}); recalculate or send weightSource Entered.");
            }
        }
    }
}
