using System.Text;

namespace AgfAdmin.Services
{
    // Een soort certificaat uit de vaste lijst: code (opgeslagen), naam, korte omschrijving en voor wie het meestal geldt. Teksten in het
    // Nederlands; de frontend heeft ze ook in het Engels. ProductsRequired: zonder producten een waarschuwing (GGN). GgnNumber: het nummer
    // is een GGN van 13 cijfers.
    public sealed record CertificateType(string Code, string Name, string Description, string TypicalHolder, bool ProductsRequired, bool GgnNumber);

    // Certificaten deel 1: de vaste lijst soorten, de controle van nummers en de status uit de einddatum. Zonder database, goed te toetsen.
    public static class CertificateRules
    {
        public const string GlobalGapIfa = "GlobalGapIfa";
        public const string GlobalGapCoc = "GlobalGapCoc";
        public const string Custom = "Custom";

        public const int MaxNumberLength = 100;
        public const int MaxCustomTypeLength = 80;
        public const int MaxBodyLength = 200;
        public const int MaxCertificatesPerOwner = 100;
        public const int MaxProductsPerCertificate = 200;
        public const int MaxCustomTypes = 50;
        public const int MaxActorLength = 100;

        // Standaard een maand van tevoren waarschuwen (instelling per bedrijf, 1 tot en met 365 dagen).
        public const int DefaultWarningDays = 30;
        public const int MinWarningDays = 1;
        public const int MaxWarningDays = 365;

        // Status uit de einddatum (nooit opgeslagen).
        public const string StatusValid = "Valid";
        public const string StatusExpiringSoon = "ExpiringSoon";
        public const string StatusExpired = "Expired";
        public const string StatusNoEndDate = "NoEndDate";

        public static readonly IReadOnlyList<CertificateType> Types = new[]
        {
            new CertificateType(GlobalGapIfa, "GLOBALG.A.P. IFA (GGN)", "Goede landbouwpraktijk op het bedrijf van de teler; het GGN is het nummer van de producent.", "Teler, producent", true, true),
            new CertificateType(GlobalGapCoc, "GLOBALG.A.P. Chain of Custody (CoC)", "Gecertificeerd product blijft herkenbaar en gescheiden in handel, opslag en verpakken.", "Handelaar, pakstation", false, false),
            new CertificateType("Grasp", "GRASP", "Aanvulling op GLOBALG.A.P. over arbeidsomstandigheden en sociale zaken.", "Teler", false, false),
            new CertificateType("Smeta", "SMETA / Sedex", "Sociale audit (arbeid, gezondheid, veiligheid, milieu, ethiek) via het Sedex-platform.", "Teler, pakstation, handelaar", false, false),
            new CertificateType("RainforestAlliance", "Rainforest Alliance", "Duurzame landbouw: natuur, klimaat en de positie van boeren en arbeiders.", "Teler, handelaar", false, false),
            new CertificateType("Fairtrade", "Fairtrade", "Eerlijke handel: minimumprijs en premie voor producenten.", "Teler, coöperatie, handelaar", false, false),
            new CertificateType("Skal", "SKAL / EU Organic (bio)", "Biologische productie en handel volgens de EU-regels; in Nederland gecontroleerd door SKAL.", "Teler, handelaar, pakstation", false, false),
            new CertificateType("Brcgs", "BRCGS", "Voedselveiligheid in verwerking, verpakken, opslag en distributie.", "Pakstation, opslag, handelaar", false, false),
            new CertificateType("Ifs", "IFS (Food, Logistics, Broker)", "Voedselveiligheid en kwaliteit voor verwerkers, logistiek en handelaren.", "Pakstation, logistiek, handelaar", false, false),
            new CertificateType("Siza", "SIZA", "Sociale en milieu-audit voor Zuid-Afrikaanse telers.", "Teler (Zuid-Afrika)", false, false),
            new CertificateType("AmforiBsci", "amfori BSCI", "Sociale audit in de toeleveringsketen.", "Teler, pakstation", false, false),
            new CertificateType("Sa8000", "SA8000", "Sociale verantwoordelijkheid en arbeidsrechten.", "Teler, pakstation", false, false),
            new CertificateType("Qs", "QS", "Kwaliteitszekering in de Duitse keten (QS-systeem).", "Teler, handelaar (Duitsland)", false, false),
            new CertificateType("Haccp", "HACCP", "Voedselveiligheid op basis van risicoanalyse en kritische punten.", "Iedereen die voedsel verwerkt of opslaat", false, false),
            new CertificateType("Iso22000", "ISO 22000 / FSSC 22000", "Managementsysteem voor voedselveiligheid.", "Pakstation, verwerker", false, false),
            new CertificateType("LeafMarque", "LEAF Marque", "Geïntegreerde, duurzame landbouw (LEAF).", "Teler", false, false),
            new CertificateType("SustainablyGrown", "Sustainably Grown", "Duurzaamheid op het bedrijf: milieu, sociaal en economisch (SCS).", "Teler", false, false),
        };

        public static CertificateType Find(string code) => Types.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

        public static bool IsKnownType(string code) => code == Custom || Find(code) != null;

        public static bool IsGgnType(string code) => code == GlobalGapIfa;

        // Naam van de soort voor teksten (eigen soort: de ingevoerde naam).
        public static string TypeName(string code, string customName) => code == Custom ? customName : Find(code)?.Name ?? code;

        // Zoeksleutel: kleine letters, alleen letters en cijfers ("DE-ÖKO-006 1234" en "de oko 0061234" vinden elkaar niet; spaties en
        // streepjes wel weg). Leeg = null.
        public static string NumberKey(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return null;
            var builder = new StringBuilder(number.Length);
            foreach (var c in number.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            }
            return builder.Length == 0 ? null : builder.ToString();
        }

        // Nummer van een certificaat: GGN 13 cijfers (spaties als scheiding mogen; die gaan eruit), andere soorten vrij (ook lange nummers).
        public static string NormalizeNumber(string typeCode, string raw)
        {
            if (raw == null) return null;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) return null;
            return IsGgnType(typeCode) ? ArticleRules.NormalizeGgn(trimmed) : trimmed;
        }

        // Het controlecijfer van een GGN (zoals een GS1-GLN): gewichten 3 en 1 van rechts, het laatste cijfer maakt het totaal deelbaar
        // door 10. Een afwijkend controlecijfer is een waarschuwing (tikfout?), geen blokkade.
        public static bool HasValidCheckDigit(string ggn)
        {
            if (!ArticleRules.IsGgn(ggn)) return false;
            var sum = 0;
            for (var i = 0; i < 12; i++)
            {
                var digit = ggn[11 - i] - '0';
                sum += i % 2 == 0 ? digit * 3 : digit;
            }
            return (10 - sum % 10) % 10 == ggn[12] - '0';
        }

        public static int DaysLeft(DateOnly validUntil, DateOnly today) => validUntil.DayNumber - today.DayNumber;

        // Geldig tot en met de einddatum. Verloopt binnenkort: nog hooguit `warningDays` dagen (op de grens van 30 dagen: 30 dagen is
        // "verloopt binnenkort", 31 is "geldig"). Zonder einddatum: geen status.
        public static string Status(DateOnly? validUntil, DateOnly today, int warningDays)
        {
            if (validUntil is not DateOnly until) return StatusNoEndDate;
            var left = DaysLeft(until, today);
            if (left < 0) return StatusExpired;
            return left <= warningDays ? StatusExpiringSoon : StatusValid;
        }

        public static bool NeedsAttention(string status) => status is StatusExpired or StatusExpiringSoon;

        // Eigen soorten in de bedrijfsinstellingen: één per regel, zonder dubbele (hoofdletterongevoelig), in de volgorde van invoer.
        public static IReadOnlyList<string> ParseCustomTypes(string stored) =>
            (stored ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public static string FormatCustomTypes(IEnumerable<string> names) => string.Join("\n", names);

        // Overname van de oude GGN-velden bij leveranciers (VendorGgns) naar certificaten van de soort GLOBALG.A.P. IFA (GGN): alles gaat mee
        // (nummer, producent, actief, volgorde, gecontroleerd op en door wie, geldig tot, GGN-label), zonder producten en dus gemarkeerd als
        // "producten nog invullen". Een al overgenomen GGN (SourceVendorGgnId) wordt overgeslagen, zodat het script veilig opnieuw kan. De oude
        // tabel blijft staan. Gewone SQL (PostgreSQL en de SQLite-tests). Migratie 66 (AddRelationCertificates) voert precies deze tekst uit:
        // niet meer wijzigen.
        public const string CopyVendorGgnsSql = """
            INSERT INTO "RelationCertificates" ("RelationId", "TypeCode", "CustomTypeName", "Number", "NumberKey", "CertificationBody", "ValidFrom", "ValidUntil",
                "ProductsPending", "ProducerName", "HasGgnLabel", "CheckedOn", "CheckedBy", "IsActive", "SortOrder", "SourceVendorGgnId",
                "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy")
            SELECT v."RelationId", 'GlobalGapIfa', NULL, g."Ggn", g."Ggn", NULL, NULL, g."CertificateValidUntil",
                TRUE, g."ProducerName", g."HasGgnLabel", g."CheckedOn", g."CheckedBy", g."IsActive", g."SortOrder", g."Id",
                CURRENT_TIMESTAMP, 'Migratie', CURRENT_TIMESTAMP, 'Migratie'
            FROM "VendorGgns" g
            JOIN "Vendors" v ON v."Id" = g."VendorId"
            WHERE NOT EXISTS (SELECT 1 FROM "RelationCertificates" c WHERE c."SourceVendorGgnId" = g."Id")
            ORDER BY g."VendorId", g."SortOrder", g."Id";
            """;
    }
}
