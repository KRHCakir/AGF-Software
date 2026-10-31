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
        public const int MaxActorLength = 100;

        // Artikelhistorie (ArticleAuditEvents.EventType).
        public const string EventCreatedFromSupply = "CreatedFromSupply";
        public const string EventCreatedFromReceipt = "CreatedFromReceipt";
        public const string EventArchived = "Archived";
        public const string EventRestored = "Restored";
        public const string EventDeleted = "Deleted";
        public const string EventProductDeleted = "ProductDeleted";

        // Een gearchiveerd artikel komt niet meer op nieuwe regels (guard-regel 19); dezelfde melding in elke service.
        public static string ArchivedMessage(int id) => $"Article {id} is archived; restore it in Articles before using it on a new line.";

        // Volledige naam voor bevestigingen en de historie: product · ras · herkomst · maat · verpakking · klasse · merk; een leeg
        // kenmerk wordt "geen" (nooit lege tekst), zodat twee artikelen die alleen in een leeg kenmerk verschillen herkenbaar blijven.
        public static string FullName(string productName, string variantName, string origin, string size, string packaging, string tradeClass, string brand)
        {
            static string Part(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            var article = string.Join(" ", new[] { Part(productName), Part(variantName) }.Where(p => p != null));
            return string.Join(" · ", new[]
            {
                article,
                Part(origin) ?? "herkomst geen",
                "maat " + (Part(size) ?? "geen"),
                "verpakking " + (Part(packaging) ?? "geen"),
                "klasse " + (Part(tradeClass) ?? "geen"),
                "merk " + (Part(brand) ?? "geen"),
            });
        }

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

        // Alleen de toegestane scheidingstekens (spatie en punt uit de gebruikelijke schrijfwijze "0804 40 00" of "0804.40.00")
        // worden verwijderd. Alle andere tekens blijven staan, zodat de validatie letters of leestekens weigert in plaats van
        // ze stilzwijgend weg te laten ("0804ABC4000" wordt nooit "08044000").
        public static string NormalizeGoodsCode(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : new string(raw.Trim().Where(c => c != ' ' && c != '.').ToArray());

        public static bool IsDigitsOnly(string normalized) => !string.IsNullOrEmpty(normalized) && normalized.All(c => c >= '0' && c <= '9');

        public static bool IsGoodsCodeFor(string system, string normalized) =>
            normalized != null && normalized.Length == GoodsCodeLength(system) && normalized.All(char.IsDigit);

        // GLOBALG.A.P.-nummer (GGN): 13 cijfers, als tekst bewaard (voorloopnullen blijven). Los van HS/GN/TARIC. Alleen de
        // schrijfwijze wordt gecontroleerd; een ingevuld nummer bewijst geen geldige certificering ("zoals opgegeven").
        public const int GgnLength = 13;

        // Alleen spaties als scheidingsteken; alle andere tekens blijven staan zodat de validatie ze weigert.
        public static string NormalizeGgn(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : new string(raw.Trim().Where(c => c != ' ').ToArray());

        public static bool IsGgn(string normalized) => normalized != null && normalized.Length == GgnLength && IsDigitsOnly(normalized);

        // Handelsklasse volgens de EU-handelsnormen voor groenten en fruit (Extra, I, II) als suggestie; vrije tekst blijft
        // mogelijk. "Industrie" is een handelsbestemming, geen klasse, en staat daarom niet in de lijst.
        public static readonly IReadOnlyList<string> TradeClassSuggestions = new[] { "Extra", "I", "II" };

        private static readonly Regex Shortcut = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.Compiled);

        public static bool IsShortcutCode(string value) => value != null && Shortcut.IsMatch(value);
    }
}
