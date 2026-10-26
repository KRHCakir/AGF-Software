using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    // Btw-nummervorm per EU-lidstaat (plus XI voor Noord-Ierland), op het genormaliseerde nummer (RelationRules.NormalizeVat:
    // hoofdletters, zonder spaties/punten/streepjes, inclusief landprefix). Griekenland gebruikt EL; GR wordt geweigerd met de
    // suggestie EL. Een land buiten de tabel valt terug op de algemene vorm (RelationRules.IsVatShape). Alleen vormcontrole, geen
    // VIES: VatVerificationStatus blijft NotChecked. Geldt alleen voor nieuwe of gewijzigde nummers. De frontend spiegelt deze tabel.
    public static class VatRules
    {
        public static readonly IReadOnlyDictionary<string, string> Patterns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AT"] = "^ATU[0-9]{8}$",
            ["BE"] = "^BE[01][0-9]{9}$",
            ["BG"] = "^BG[0-9]{9,10}$",
            ["CY"] = "^CY[0-9]{8}[A-Z]$",
            ["CZ"] = "^CZ[0-9]{8,10}$",
            ["DE"] = "^DE[0-9]{9}$",
            ["DK"] = "^DK[0-9]{8}$",
            ["EE"] = "^EE[0-9]{9}$",
            ["EL"] = "^EL[0-9]{9}$",
            ["ES"] = "^ES[A-Z0-9][0-9]{7}[A-Z0-9]$",
            ["FI"] = "^FI[0-9]{8}$",
            ["FR"] = "^FR[A-HJ-NP-Z0-9]{2}[0-9]{9}$",
            ["HR"] = "^HR[0-9]{11}$",
            ["HU"] = "^HU[0-9]{8}$",
            ["IE"] = "^IE([0-9]{7}[A-W][A-IW]?|[0-9][A-Z][0-9]{5}[A-W])$",
            ["IT"] = "^IT[0-9]{11}$",
            ["LT"] = "^LT([0-9]{9}|[0-9]{12})$",
            ["LU"] = "^LU[0-9]{8}$",
            ["LV"] = "^LV[0-9]{11}$",
            ["MT"] = "^MT[0-9]{8}$",
            ["NL"] = "^NL[0-9]{9}B[0-9]{2}$",
            ["PL"] = "^PL[0-9]{10}$",
            ["PT"] = "^PT[0-9]{9}$",
            ["RO"] = "^RO[1-9][0-9]{1,9}$",
            ["SE"] = "^SE[0-9]{10}01$",
            ["SI"] = "^SI[0-9]{8}$",
            ["SK"] = "^SK[0-9]{10}$",
            ["XI"] = "^XI([0-9]{9}|[0-9]{12}|GD[0-4][0-9]{2}|HA[5-9][0-9]{2})$",
        };

        // Voorbeeld per land voor de foutmelding.
        public static readonly IReadOnlyDictionary<string, string> Examples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AT"] = "ATU12345678", ["BE"] = "BE0123456789", ["BG"] = "BG123456789", ["CY"] = "CY12345678X", ["CZ"] = "CZ12345678",
            ["DE"] = "DE123456789", ["DK"] = "DK12345678", ["EE"] = "EE123456789", ["EL"] = "EL123456789", ["ES"] = "ESX1234567X",
            ["FI"] = "FI12345678", ["FR"] = "FRXX123456789", ["HR"] = "HR12345678901", ["HU"] = "HU12345678", ["IE"] = "IE1234567X",
            ["IT"] = "IT12345678901", ["LT"] = "LT123456789", ["LU"] = "LU12345678", ["LV"] = "LV12345678901", ["MT"] = "MT12345678",
            ["NL"] = "NL123456789B01", ["PL"] = "PL1234567890", ["PT"] = "PT123456789", ["RO"] = "RO1234567890", ["SE"] = "SE123456789001",
            ["SI"] = "SI12345678", ["SK"] = "SK1234567890", ["XI"] = "XI123456789",
        };

        private static readonly Dictionary<string, Regex> Compiled = Patterns.ToDictionary(p => p.Key, p => new Regex(p.Value, RegexOptions.Compiled), StringComparer.Ordinal);

        public enum VatProblem { None, Shape, CountryPattern, GreeceUsesEl }

        // Uitkomst van de controle op een genormaliseerd nummer (null = geen nummer = geen probleem).
        public static VatProblem Check(string normalized)
        {
            if (normalized == null) return VatProblem.None;
            if (!RelationRules.IsVatShape(normalized)) return VatProblem.Shape;
            var country = normalized[..2];
            if (country == "GR") return VatProblem.GreeceUsesEl;
            if (Compiled.TryGetValue(country, out var pattern)) return pattern.IsMatch(normalized) ? VatProblem.None : VatProblem.CountryPattern;
            return VatProblem.None;
        }

        // Suggestie bij GR: hetzelfde nummer met EL (alleen als dat wel de Griekse vorm heeft).
        public static string GreekSuggestion(string normalized) =>
            normalized != null && normalized.StartsWith("GR", StringComparison.Ordinal) && Compiled["EL"].IsMatch("EL" + normalized[2..]) ? "EL" + normalized[2..] : null;

        public static string Message(VatProblem problem, string normalized) => problem switch
        {
            VatProblem.Shape => "Een btw-nummer begint met een landcode van twee letters (XI voor Noord-Ierland, EL voor Griekenland), gevolgd door letters en cijfers. Spaties, punten en streepjes tellen niet mee. Dit is een vormcontrole, geen VIES-controle.",
            VatProblem.CountryPattern => $"Dit btw-nummer heeft niet de vorm van een {normalized[..2]}-nummer (bijvoorbeeld {Examples[normalized[..2]]}). Dit is een vormcontrole, geen VIES-controle.",
            VatProblem.GreeceUsesEl => GreekSuggestion(normalized) is { } suggestion
                ? $"Griekse btw-nummers beginnen met EL, niet GR: gebruik {suggestion}."
                : "Griekse btw-nummers beginnen met EL, niet GR (bijvoorbeeld EL123456789).",
            _ => null
        };
    }
}
