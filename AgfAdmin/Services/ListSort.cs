using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AgfAdmin.Services
{
    // Sorteren op de server voor lijsten die op de server bladeren (feedback 22 november, prompt 2): de client stuurt `sort` (een veld
    // uit de toegestane lijst van die API, nooit een vrije veldnaam of kolom) en `direction` (asc of desc). Een onbekend veld of een
    // onbekende richting geeft een validatiefout (400). Er wordt gesorteerd over alle rijen na de filters, daarna gepagineerd.
    // Per soort de juiste volgorde, gelijk aan de website (lib/listSort.ts): tekst zonder verschil tussen hoofdletters en accenten,
    // getallen en datums als getal en datum, nummers natuurlijk (2 voor 10, ook met letters zoals I- of V-). Een lege waarde staat in
    // beide richtingen onderaan; gelijke waarden houden de standaardvolgorde (OrderBy is stabiel).
    public enum SortKind { Text, Natural, Number, Date }

    public sealed record SortRequest(string Field, bool Descending);

    public sealed class SortField<T>
    {
        public SortField(SortKind kind, Func<T, object> value) { Kind = kind; Value = value; }
        public SortKind Kind { get; }
        public Func<T, object> Value { get; }
    }

    public static class ListSort
    {
        public const string Ascending = "asc";
        public const string Descending = "desc";

        // Leeg = de standaardvolgorde (null). Hoofdletterongevoelig, maar altijd terug naar een veldnaam uit de toegestane lijst.
        public static SortRequest Parse(string sort, string direction, IEnumerable<string> allowed, IDictionary<string, string[]> errors)
        {
            var fields = allowed.ToList();
            if (string.IsNullOrWhiteSpace(sort))
            {
                if (!string.IsNullOrWhiteSpace(direction) && !IsDirection(direction)) errors["direction"] = new[] { "Direction must be asc or desc." };
                return null;
            }
            var field = fields.FirstOrDefault(f => string.Equals(f, sort.Trim(), StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                errors["sort"] = new[] { $"Sort must be one of: {string.Join(", ", fields)}." };
                return null;
            }
            if (!string.IsNullOrWhiteSpace(direction) && !IsDirection(direction))
            {
                errors["direction"] = new[] { "Direction must be asc or desc." };
                return null;
            }
            return new SortRequest(field, string.Equals(direction?.Trim(), Descending, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsDirection(string direction) =>
            string.Equals(direction.Trim(), Ascending, StringComparison.OrdinalIgnoreCase) || string.Equals(direction.Trim(), Descending, StringComparison.OrdinalIgnoreCase);

        // Sorteert `items` (al in de standaardvolgorde) op het gevraagde veld; zonder verzoek blijft de volgorde.
        public static List<T> Apply<T>(IEnumerable<T> items, SortRequest request, IReadOnlyDictionary<string, SortField<T>> fields)
        {
            if (request == null || !fields.TryGetValue(request.Field, out var field)) return items.ToList();
            var comparer = new ValueComparer(field.Kind);
            var keyed = items.Select(item => (Item: item, Value: field.Value(item))).ToList();
            // Leeg onderaan in beide richtingen: eerst op "is leeg", daarna op de waarde in de gevraagde richting.
            var ordered = keyed.OrderBy(k => IsEmpty(k.Value));
            ordered = request.Descending ? ordered.ThenByDescending(k => k.Value, comparer) : ordered.ThenBy(k => k.Value, comparer);
            return ordered.Select(k => k.Item).ToList();
        }

        public static bool IsEmpty(object value) => value == null || (value is string text && string.IsNullOrWhiteSpace(text));

        private sealed class ValueComparer : IComparer<object>
        {
            private readonly SortKind _kind;
            public ValueComparer(SortKind kind) { _kind = kind; }

            public int Compare(object x, object y)
            {
                if (IsEmpty(x) || IsEmpty(y)) return IsEmpty(x) == IsEmpty(y) ? 0 : IsEmpty(x) ? 1 : -1;
                return _kind switch
                {
                    SortKind.Number => Convert.ToDecimal(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(y, CultureInfo.InvariantCulture)),
                    SortKind.Date => ToDate(x).CompareTo(ToDate(y)),
                    SortKind.Natural => CompareNatural(x.ToString(), y.ToString()),
                    _ => CompareText(x.ToString(), y.ToString()),
                };
            }

            private static DateTime ToDate(object value) => value switch
            {
                DateTime dateTime => dateTime,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                DateTimeOffset offset => offset.UtcDateTime,
                _ => DateTime.Parse(value.ToString(), CultureInfo.InvariantCulture),
            };
        }

        // Tekst zonder hoofdletters en accenten ("é" = "e"); daarna gewoon alfabetisch. Niet afhankelijk van de taalinstelling van de
        // server, zodat de demo en de tests hetzelfde sorteren.
        public static string TextKey(string value)
        {
            var decomposed = (value ?? "").Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static int CompareText(string x, string y) => string.CompareOrdinal(TextKey(x), TextKey(y));

        // Natuurlijk: reeksen cijfers als getal (zonder voorloopnullen; bij gelijk getal de kortste eerst), de rest als tekst.
        public static int CompareNatural(string x, string y)
        {
            var a = TextKey(x);
            var b = TextKey(y);
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    var startA = i; var startB = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var digitsA = a[startA..i].TrimStart('0');
                    var digitsB = b[startB..j].TrimStart('0');
                    if (digitsA.Length != digitsB.Length) return digitsA.Length.CompareTo(digitsB.Length);
                    var byDigits = string.CompareOrdinal(digitsA, digitsB);
                    if (byDigits != 0) return byDigits;
                    var byLength = (i - startA).CompareTo(j - startB);
                    if (byLength != 0) return byLength;
                }
                else
                {
                    if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
