using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    // IBAN- en BIC-regels voor bankrekeningen van relaties. Puur en deterministisch; de frontend spiegelt ze (lengtetabel, mod-97, BIC-vorm).
    // Een geldige IBAN bewijst alleen de vorm en het controlegetal, niet dat de rekening bestaat of van de relatie is: daarom blijft
    // elke nieuwe of gewijzigde rekening Unconfirmed tot iemand haar bevestigt.
    public static class BankAccountRules
    {
        public const int MaxAccountHolderLength = 200;
        public const int MaxOtherAccountNumberLength = 64;
        public const int MaxBankAccounts = 20;

        // IBAN-lengte per land (SWIFT IBAN-register). Een land buiten deze tabel wordt geweigerd: gebruik dan "ander rekeningnummer".
        public static readonly IReadOnlyDictionary<string, int> IbanLengths = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["AD"] = 24, ["AE"] = 23, ["AL"] = 28, ["AT"] = 20, ["AZ"] = 28, ["BA"] = 20, ["BE"] = 16, ["BG"] = 22, ["BH"] = 22, ["BI"] = 27,
            ["BR"] = 29, ["BY"] = 28, ["CH"] = 21, ["CR"] = 22, ["CY"] = 28, ["CZ"] = 24, ["DE"] = 22, ["DJ"] = 27, ["DK"] = 18, ["DO"] = 28,
            ["EE"] = 20, ["EG"] = 29, ["ES"] = 24, ["FI"] = 18, ["FK"] = 18, ["FO"] = 18, ["FR"] = 27, ["GB"] = 22, ["GE"] = 22, ["GI"] = 23,
            ["GL"] = 18, ["GR"] = 27, ["GT"] = 28, ["HN"] = 28, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IL"] = 23, ["IQ"] = 23, ["IS"] = 26,
            ["IT"] = 27, ["JO"] = 30, ["KW"] = 30, ["KZ"] = 20, ["LB"] = 28, ["LC"] = 32, ["LI"] = 21, ["LT"] = 20, ["LU"] = 20, ["LV"] = 21,
            ["LY"] = 25, ["MC"] = 27, ["MD"] = 24, ["ME"] = 22, ["MK"] = 19, ["MN"] = 20, ["MR"] = 27, ["MT"] = 31, ["MU"] = 30, ["NI"] = 28,
            ["NL"] = 18, ["NO"] = 15, ["OM"] = 23, ["PK"] = 24, ["PL"] = 28, ["PS"] = 29, ["PT"] = 25, ["QA"] = 29, ["RO"] = 24, ["RS"] = 22,
            ["RU"] = 33, ["SA"] = 24, ["SC"] = 31, ["SD"] = 18, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["SO"] = 23, ["ST"] = 25,
            ["SV"] = 28, ["TL"] = 23, ["TN"] = 24, ["TR"] = 26, ["UA"] = 29, ["VA"] = 22, ["VG"] = 24, ["XK"] = 20, ["YE"] = 30,
        };

        private static readonly Regex IbanShape = new("^[A-Z]{2}[0-9]{2}[A-Z0-9]+$", RegexOptions.Compiled);
        // BIC: 4 letters bank, 2 letters land, 2 tekens locatie, optioneel 3 tekens filiaal (AAAABBCC of AAAABBCCDDD).
        private static readonly Regex BicShape = new("^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.Compiled);

        // Normaliseren: spaties (ook niet-afbrekende) weg, hoofdletters. Leeg -> null.
        public static string NormalizeIban(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        public static string NormalizeBic(string raw) => NormalizeIban(raw);

        public enum IbanProblem { None, Shape, UnknownCountry, Length, Checksum }

        // Controle in vaste volgorde: vorm, land bekend, lengte voor dat land, mod-97 = 1.
        public static IbanProblem CheckIban(string normalized)
        {
            if (normalized == null || !IbanShape.IsMatch(normalized)) return IbanProblem.Shape;
            if (!IbanLengths.TryGetValue(normalized[..2], out var length)) return IbanProblem.UnknownCountry;
            if (normalized.Length != length) return IbanProblem.Length;
            return Mod97(normalized) == 1 ? IbanProblem.None : IbanProblem.Checksum;
        }

        public static bool IsValidIban(string normalized) => CheckIban(normalized) == IbanProblem.None;

        // ISO 7064 mod 97-10: eerste vier tekens naar achteren, letters A=10 … Z=35, rest bij deling door 97 (stapsgewijs).
        public static int Mod97(string iban)
        {
            var rearranged = iban[4..] + iban[..4];
            var remainder = 0;
            foreach (var c in rearranged)
            {
                var value = char.IsDigit(c) ? c - '0' : c - 'A' + 10;
                remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
            }
            return remainder;
        }

        public static bool IsBic(string normalized) => normalized != null && BicShape.IsMatch(normalized);

        public static string IbanMessage(IbanProblem problem, string normalized) => problem switch
        {
            IbanProblem.Shape => "Een IBAN begint met twee letters (land) en twee cijfers, gevolgd door letters en cijfers.",
            IbanProblem.UnknownCountry => $"Landcode {normalized?[..Math.Min(2, normalized.Length)]} heeft geen IBAN; gebruik 'ander rekeningnummer'.",
            IbanProblem.Length => $"Een IBAN uit {normalized[..2]} heeft precies {IbanLengths[normalized[..2]]} tekens (nu {normalized.Length}).",
            IbanProblem.Checksum => "Het controlegetal van deze IBAN klopt niet; controleer op een tikfout.",
            _ => null
        };
    }
}
