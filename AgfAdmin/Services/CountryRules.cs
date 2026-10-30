using System.Globalization;
using System.Text;

namespace AgfAdmin.Services
{
    // Herkomst als vaste landenlijst: vergelijkingssleutels, eenduidig koppelen van oude herkomstteksten en regels voor eigen aliassen.
    // Gespiegeld in de migratie AddCountries (SQL met translate) en in agf-frontend/src/lib/countries.ts (zoeken in de landkiezer).
    public static class CountryRules
    {
        public const int MaxAliasLength = 40;

        private static readonly IReadOnlyDictionary<string, CountryList.Entry> ByCode =
            CountryList.All.ToDictionary(c => c.Code, StringComparer.Ordinal);

        public static bool IsCode(string code) => code != null && ByCode.ContainsKey(code);

        public static CountryList.Entry Find(string code) => code != null && ByCode.TryGetValue(code, out var entry) ? entry : null;

        // Getrimd, kleine letters, accenten weg en meerdere spaties als één: "  Brazilië " en "brazilie" geven dezelfde sleutel.
        public static string Key(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var space = false;
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsWhiteSpace(ch))
                {
                    space = true;
                    continue;
                }
                if (space && builder.Length > 0) builder.Append(' ');
                space = false;
                builder.Append(ch);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        // Landen waarvan de code, Nederlandse of Engelse naam dezelfde sleutel heeft als de tekst.
        public static IReadOnlyList<string> Matches(string text)
        {
            var key = Key(text);
            if (key == null) return Array.Empty<string>();
            return CountryList.All.Where(c => Key(c.Code) == key || Key(c.NameNl) == key || Key(c.NameEn) == key).Select(c => c.Code).ToList();
        }

        // Alleen een eenduidige tekst ("PE", "Peru", "peru") wordt automatisch een land; "Peru (DEMO)", "EU" of "Divers" niet.
        public static string Unambiguous(string text)
        {
            var matches = Matches(text);
            return matches.Count == 1 ? matches[0] : null;
        }

        // Fout bij een nieuwe alias, of null: leeg, te lang, gelijk aan code of naam van een ander land, of al in gebruik.
        public static string AliasConflict(string alias, string countryCode, IEnumerable<(string Key, string CountryCode)> existing)
        {
            var key = Key(alias);
            if (key == null) return "An alias is required.";
            if (alias.Trim().Length > MaxAliasLength) return $"An alias may contain at most {MaxAliasLength} characters.";
            if (!IsCode(countryCode)) return $"Country {countryCode} is not in the country list.";
            var other = Matches(alias).FirstOrDefault(code => code != countryCode);
            if (other != null) return $"'{alias.Trim()}' is the code or name of {Find(other).NameNl}; an alias can never point to another country.";
            var taken = existing.FirstOrDefault(a => a.Key == key);
            if (taken.Key != null) return taken.CountryCode == countryCode
                ? $"Alias '{alias.Trim()}' already exists for {Find(countryCode).NameNl}."
                : $"Alias '{alias.Trim()}' already points to {Find(taken.CountryCode)?.NameNl ?? taken.CountryCode}; an alias points to exactly one country.";
            return null;
        }
    }
}
