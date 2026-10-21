using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    // Regels van de artikelwerkplek (Products/ProductVariants). Een artikel is één ProductVariant onder één Product
    // (productsoort). Zoekcode (ShortcutCode) en een herkenbare omschrijving staan centraal; al het andere mag leeg blijven.
    public static class ArticleRules
    {
        public const int MaxNameLength = 100;
        public const int MaxTextLength = 100;
        public const int MaxShortcutLength = 32;
        public const int MaxNotesLength = 500;
        public const int MaxTradeClassLength = 50;

        // Codestelsels voor goederencodes: HS (WCO, 6 cijfers), CN/GN (EU gecombineerde nomenclatuur, 8 cijfers),
        // TARIC (EU geïntegreerd tarief, 10 cijfers). Een code wordt als tekst bewaard, zodat voorloopnullen blijven.
        public const string SystemHs = "HS";
        public const string SystemCn = "CN";
        public const string SystemTaric = "TARIC";
        public static readonly IReadOnlyList<string> GoodsCodeSystems = new[] { SystemHs, SystemCn, SystemTaric };

        public static int GoodsCodeLength(string system) => system switch
        {
            SystemHs => 6,
            SystemCn => 8,
            SystemTaric => 10,
            _ => 0
        };

        public static bool IsGoodsCodeSystem(string value) => value != null && GoodsCodeSystems.Contains(value, StringComparer.Ordinal);

        // Alleen cijfers; spaties en punten uit de gebruikelijke schrijfwijze ("0804 40 00") worden verwijderd.
        public static string NormalizeGoodsCode(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : new string(raw.Where(char.IsDigit).ToArray());

        public static bool IsGoodsCodeFor(string system, string normalized) =>
            normalized != null && normalized.Length == GoodsCodeLength(system) && normalized.All(char.IsDigit);

        // Handelsklasse volgens de EU-handelsnormen voor groenten en fruit (Extra, I, II) als suggestie; vrije tekst blijft
        // mogelijk. "Industrie" is een handelsbestemming, geen klasse, en staat daarom niet in de lijst.
        public static readonly IReadOnlyList<string> TradeClassSuggestions = new[] { "Extra", "I", "II" };

        private static readonly Regex Shortcut = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.Compiled);

        public static bool IsShortcutCode(string value) => value != null && Shortcut.IsMatch(value);
    }
}
