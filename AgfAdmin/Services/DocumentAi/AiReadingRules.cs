using System.Globalization;
using System.Text.Json.Nodes;

namespace AgfAdmin.Services.DocumentAi
{
    // Een controle op het voorstel: een veld van het factuurformulier (vendor, invoiceNumber, lines[0].amount, totalNet, ...) en wat er
    // mis is. TriggersFallback: dan leest het vangnetmodel het document nog één keer.
    public sealed record ReadingProblem(string Field, string Code, bool TriggersFallback = false);

    // Regels voor AI-lezen zonder database of netwerk (met tests): statussen, de controles op het voorstel, kosten en eenheden.
    public static class AiReadingRules
    {
        public const string StatusProposed = "Proposed";
        public const string StatusApproved = "Approved";
        public const string StatusRejected = "Rejected";
        public const string StatusManual = "Manual";
        public const string StatusFailed = "Failed";
        public const string DocumentPurchaseInvoice = "PurchaseInvoice";

        // Codes van de controles (de frontend toont er een korte tekst bij).
        public const string Missing = "missing";             // niet gevonden
        public const string Uncertain = "uncertain";         // de AI twijfelt
        public const string LinesMismatch = "linesMismatch"; // totaal excl. btw klopt niet met de regels
        public const string VatMismatch = "vatMismatch";     // btw rekent niet
        public const string GrossMismatch = "grossMismatch"; // excl. + btw is niet het totaal incl.
        public const string LineMismatch = "lineMismatch";   // aantal x prijs is niet het regelbedrag
        public const string InvalidDate = "invalidDate";

        public const decimal DefaultMonthlyLimitEur = 25m;
        public const decimal MaxMonthlyLimitEur = 100_000m;
        private const decimal Tolerance = 0.02m;

        private static readonly Dictionary<string, string> HeaderKeys = new()
        {
            ["leverancier"] = "vendor", ["factuurnummer"] = "invoiceNumber", ["datum"] = "invoiceDate", ["valuta"] = "currency",
            ["ordernummer_leverancier"] = "supplierOrderNumber", ["totaal_excl"] = "totalNet", ["btw_totaal"] = "totalVat", ["totaal_incl"] = "totalGross",
            ["leverancier_btw"] = "vendor", ["leverancier_kvk"] = "vendor", ["leverancier_iban"] = "vendor",
        };
        private static readonly Dictionary<string, string> LineKeys = new()
        {
            ["omschrijving"] = "description", ["aantal"] = "quantity", ["eenheid"] = "unit", ["prijs"] = "unitPrice", ["bedrag"] = "amount", ["btw_percentage"] = "vatPercentage",
        };
        // Een twijfel bij deze velden of het ontbreken ervan laat het vangnet lezen; de rest is alleen een gele markering.
        private static readonly HashSet<string> Required = new() { "vendor", "invoiceNumber", "invoiceDate", "totalNet", "totalGross" };

        // Veldnaam van het model ("factuurnummer", "regels[2].bedrag") naar het veld van het formulier; onbekend: null.
        public static string FormField(string aiField)
        {
            var field = (aiField ?? "").Trim();
            if (HeaderKeys.TryGetValue(field, out var header)) return header;
            var open = field.IndexOf('[');
            var close = field.IndexOf(']');
            if (field.StartsWith("regels[", StringComparison.Ordinal) && close > open && int.TryParse(field[(open + 1)..close], out var index)
                && field.Length > close + 2 && LineKeys.TryGetValue(field[(close + 2)..], out var line))
            {
                return $"lines[{index}].{line}";
            }
            return null;
        }

        public static string Text(JsonNode node) => node is JsonValue value && value.TryGetValue(out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
        public static decimal? Number(JsonNode node) => node is JsonValue value && value.TryGetValue(out decimal number) ? number : null;
        public static DateOnly? Date(JsonNode node) =>
            DateOnly.TryParseExact(Text(node) ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

        // Alle controles op een geldig voorstel (zie ExtractionSchema). Ontbrekende waarden blijven leeg: niets wordt aangevuld of verzonnen.
        public static List<ReadingProblem> Problems(JsonObject proposal)
        {
            var problems = new List<ReadingProblem>();
            void Add(string field, string code, bool fallback) { if (!problems.Any(p => p.Field == field && p.Code == code)) problems.Add(new ReadingProblem(field, code, fallback)); }

            if (Text(proposal["leverancier"]) == null) Add("vendor", Missing, true);
            if (Text(proposal["factuurnummer"]) == null) Add("invoiceNumber", Missing, true);
            if (Text(proposal["datum"]) == null) Add("invoiceDate", Missing, true);
            else if (Date(proposal["datum"]) == null) Add("invoiceDate", InvalidDate, true);
            if (Text(proposal["valuta"]) == null) Add("currency", Missing, false);

            var lines = (proposal["regels"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            if (lines.Count == 0) Add("lines", Missing, true);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (Text(line["omschrijving"]) == null) Add($"lines[{i}].description", Missing, false);
                var quantity = Number(line["aantal"]);
                var price = Number(line["prijs"]);
                var amount = Number(line["bedrag"]);
                if (quantity == null) Add($"lines[{i}].quantity", Missing, false);
                if (price == null) Add($"lines[{i}].unitPrice", Missing, false);
                if (amount == null) Add($"lines[{i}].amount", Missing, false);
                // Zonder btw-percentage wordt de btw-controle overgeslagen: markeren, geen vangnet (verlegde btw/export kent geen percentage).
                if (Number(line["btw_percentage"]) == null) Add($"lines[{i}].vatPercentage", Missing, false);
                // Een korting op de regel kan het bedrag lager maken dan aantal x prijs: alleen markeren, geen vangnet.
                if (quantity != null && price != null && amount != null && Math.Abs(quantity.Value * price.Value - amount.Value) > Math.Max(Tolerance, Math.Abs(amount.Value) * 0.005m))
                {
                    Add($"lines[{i}].amount", LineMismatch, false);
                }
            }

            var net = Number(proposal["totaal_excl"]);
            var vat = Number(proposal["btw_totaal"]);
            var gross = Number(proposal["totaal_incl"]);
            if (net == null) Add("totalNet", Missing, true);
            if (gross == null) Add("totalGross", Missing, true);
            // Zonder btw-totaal worden de btw- en bruto-controle overgeslagen: markeren. Geen vangnet, want een factuur zonder btw (verlegd, export) heeft dit veld legitiem niet.
            if (vat == null) Add("totalVat", Missing, false);
            var amounts = lines.Select(l => Number(l["bedrag"])).ToList();
            if (net != null && lines.Count > 0 && amounts.All(a => a != null) && Math.Abs(amounts.Sum(a => a.Value) - net.Value) > Tolerance)
            {
                Add("totalNet", LinesMismatch, true);
            }
            var rates = lines.Select(l => Number(l["btw_percentage"])).ToList();
            if (vat != null && lines.Count > 0 && amounts.All(a => a != null) && rates.All(r => r != null))
            {
                // Btw per regel afgerond; afronding op documentniveau mag één cent per regel schelen.
                var expected = amounts.Zip(rates, (a, r) => decimal.Round(a.Value * r.Value / 100m, 2, MidpointRounding.AwayFromZero)).Sum();
                if (Math.Abs(expected - vat.Value) > Math.Max(Tolerance, 0.01m * lines.Count)) Add("totalVat", VatMismatch, true);
            }
            if (net != null && vat != null && gross != null && Math.Abs(net.Value + vat.Value - gross.Value) > Tolerance) Add("totalGross", GrossMismatch, true);

            foreach (var certainty in (proposal["zekerheden"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (Text(certainty["zekerheid"]) != "laag") continue;
                var field = FormField(Text(certainty["veld"]));
                if (field != null) Add(field, Uncertain, Required.Contains(field));
            }
            return problems;
        }

        public static bool NeedsFallback(IEnumerable<ReadingProblem> problems) => problems.Any(p => p.TriggersFallback);

        // Kosten van één verzoek in dollars; boven de staffel gelden de hogere prijzen voor het hele verzoek.
        public static decimal CostUsd(DocumentAiModelPrice price, long inputTokens, long outputTokens)
        {
            var (input, output) = Rates(price, inputTokens);
            return decimal.Round((inputTokens * input + outputTokens * output) / 1_000_000m, 6, MidpointRounding.AwayFromZero);
        }

        // De prijzen per miljoen tokens die voor dit verzoek gelden.
        public static (decimal Input, decimal Output) Rates(DocumentAiModelPrice price, long inputTokens) =>
            price.TierAboveInputTokens is long above && inputTokens > above && price.TierInputPerMillionUsd != null && price.TierOutputPerMillionUsd != null
                ? (price.TierInputPerMillionUsd.Value, price.TierOutputPerMillionUsd.Value)
                : (price.InputPerMillionUsd, price.OutputPerMillionUsd);

        public static decimal ToEur(decimal usd, decimal eurPerUsd) => decimal.Round(usd * eurPerUsd, 6, MidpointRounding.AwayFromZero);

        // Eenheid zoals op de factuur ("dozen", "colli", "stuks") naar een eenheid van de regel; onbekend (ook kg): leeg, nooit geraden.
        public static string Unit(string text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "doos" or "dozen" or "box" or "boxes" or "ds" or "karton" or "carton" or "cartons" or "kist" or "kisten" or "krt" => "box",
            "colli" or "coll" or "col" or "cll" or "pkg" or "packages" => "colli",
            "tray" or "trays" or "tr" => "tray",
            "stuk" or "stuks" or "st" or "stk" or "pc" or "pcs" or "piece" or "pieces" or "each" or "ea" => "piece",
            "dozijn" or "dozijnen" or "dz" => "dozen",
            _ => null,
        };
    }
}
