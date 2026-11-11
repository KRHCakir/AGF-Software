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

        // Vergelijksleutel voor kenmerken van een artikel: hoofdletters, accenten en dubbele spaties tellen niet, leeg = null = "".
        // "Test" en "test" zijn dus hetzelfde kenmerk; zelfde regel als countryKey in de frontend (lib/countries.ts).
        public static string AttributeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }
            var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
            var letters = decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
            return Regex.Replace(new string(letters.ToArray()), @"\s+", " ").ToLowerInvariant();
        }

        // Ingetypte tekst zoals hij wordt bewaard: randen eraf en dubbele spaties enkel; leeg = null. Hoofdletters blijven zoals getypt.
        public static string TypedText(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value.Trim(), @"\s+", " ");

        public const string SourceSupply = "Supply";
        public const string SourceReceipt = "Receipt";

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

        // Handelsklasse: een vaste lijst met de drie EU-klassen voor groenten en fruit. Bewaard wordt altijd de vaste waarde, nooit de
        // getypte schrijfwijze. "Industrie" is een handelsbestemming, geen klasse, en staat daarom niet in de lijst.
        public const string ClassExtra = "Extra";
        public const string ClassI = "I";
        public const string ClassII = "II";
        public static readonly IReadOnlyList<string> TradeClasses = new[] { ClassExtra, ClassI, ClassII };
        public static readonly IReadOnlyList<string> TradeClassSuggestions = TradeClasses;

        // Voorvoegsels die niet meetellen ("klasse 1", "class I", "cat. 1", "Kl. II"); daarna tellen spaties, punten, dubbele punten,
        // streepjes en liggende streepjes niet. Dezelfde regels staan in de frontend (lib/tradeClass.ts) en in de migratie
        // NormalizeTradeClasses; een test houdt die drie gelijk.
        public const string TradeClassPrefixPattern = @"^(handelsklasse|klasse|klas|kl|class|categorie|category|cat)[\s.:]*";
        public const string TradeClassSeparatorPattern = @"[\s.:_-]";

        // De vaste klasse bij een getypte waarde: "1", "i", "I", "klasse 1", "class I", "cat. 1" = I; "2", "ii", "klasse 2" = II;
        // "extra", "0", "e", "klasse extra" = Extra. null bij leeg en bij alles wat niet eenduidig één klasse is (nooit raden).
        public static string NormalizeTradeClass(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var key = Regex.Replace(value.Trim().ToLowerInvariant(), TradeClassPrefixPattern, "");
            key = Regex.Replace(key, TradeClassSeparatorPattern, "");
            return key switch
            {
                "extra" or "e" or "0" => ClassExtra,
                "i" or "1" => ClassI,
                "ii" or "2" => ClassII,
                _ => null
            };
        }

        public static bool IsTradeClass(string value) => value != null && TradeClasses.Contains(value, StringComparer.Ordinal);

        // Vergelijksleutel voor een klasse: de vaste klasse als die herkend wordt ("1" en "I" zijn hetzelfde), anders de tekst zelf
        // (een oude, niet herkende waarde is alleen gelijk aan dezelfde tekst); leeg = "".
        public static string TradeClassKey(string value) => NormalizeTradeClass(value) ?? AttributeKey(value);

        // Maat is verplicht; heeft een product echt geen maat, dan is "n.v.t." een gewone waarde die ook vergeleken wordt.
        public const string SizeNotApplicable = "n.v.t.";

        // Maat en klasse van een nieuw of gewijzigd artikel: allebei verplicht; de klasse wordt de vaste waarde (Extra, I, II).
        // Bestaande artikelen zonder maat of klasse blijven bruikbaar (en staan in "Opschonen"); pas bij bewerken moeten ze gevuld.
        public static (string Size, string TradeClass) RequiredSizeAndClass(string size, string tradeClass, Action<string, string> addError)
        {
            var typedSize = TypedText(size);
            if (typedSize == null)
            {
                addError("sizeCategory", $"Enter the size; use \"{SizeNotApplicable}\" when the product really has no size.");
            }
            else if (typedSize.Length > MaxTextLength)
            {
                addError("sizeCategory", $"This field may contain at most {MaxTextLength} characters.");
                typedSize = null;
            }
            // Eerst de lengte: een lange tekst wordt nooit genormaliseerd (ook niet "I" gevolgd door duizenden spaties).
            var tooLong = tradeClass != null && tradeClass.Length > MaxTradeClassLength;
            var normalized = tooLong ? null : NormalizeTradeClass(tradeClass);
            if (string.IsNullOrWhiteSpace(tradeClass))
            {
                addError("tradeClass", "Choose the class: Extra, I or II.");
            }
            else if (tooLong)
            {
                addError("tradeClass", $"This field may contain at most {MaxTradeClassLength} characters.");
            }
            else if (normalized == null)
            {
                addError("tradeClass", $"Class '{tradeClass.Trim()}' is not in the list; choose Extra, I or II.");
            }
            return (typedSize, normalized);
        }

        private static readonly Regex Shortcut = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.Compiled);

        public static bool IsShortcutCode(string value) => value != null && Shortcut.IsMatch(value);

        // Genormaliseerde snelcode voor de unieke index en het opzoeken: spaties eromheen weg, kleine letters; leeg = geen snelcode.
        // Precies zoals de migratie bestaande codes vult (PostgreSQL trim() haalt alleen spaties weg), zodat beide dezelfde sleutel geven.
        // Andere witruimte kan in een snelcode niet voorkomen: Artikelen weigert die, de oude productpagina trimt vóór het opslaan.
        public static string ShortcutKey(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim(' ').ToLowerInvariant();
    }
}
