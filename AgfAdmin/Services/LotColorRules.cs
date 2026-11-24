namespace AgfAdmin.Services
{
    // Kleur van een partij (feedback 22 november): een eigen lijst per product (Stamgegevens), een waarde op de inkoop-partij.
    public static class LotColorRules
    {
        public const int MaxColors = 20;
        public const int MaxColorLength = 40;
        public const int MaxStoredLength = 1000;

        public static List<string> Parse(string stored) =>
            string.IsNullOrWhiteSpace(stored) ? new List<string>() : stored.Split('\n').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

        // Getrimd, zonder lege en dubbele waarden (hoofdletters tellen niet), in de opgegeven volgorde. Null als de lijst niet klopt.
        public static List<string> Normalize(IEnumerable<string> values, Dictionary<string, string[]> errors)
        {
            var result = new List<string>();
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                var color = value?.Trim();
                if (string.IsNullOrEmpty(color) || result.Contains(color, StringComparer.OrdinalIgnoreCase)) continue;
                if (color.Length > MaxColorLength || color.Contains('\n'))
                {
                    errors["colors"] = new[] { $"A color has at most {MaxColorLength} characters on one line." };
                    return null;
                }
                result.Add(color);
            }
            if (result.Count > MaxColors)
            {
                errors["colors"] = new[] { $"At most {MaxColors} colors per product." };
                return null;
            }
            return result;
        }

        public static string Join(IReadOnlyCollection<string> colors) => colors.Count == 0 ? null : string.Join("\n", colors);
    }
}
