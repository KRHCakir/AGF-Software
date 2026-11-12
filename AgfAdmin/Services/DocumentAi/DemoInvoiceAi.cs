using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgfDocumentAi;

namespace AgfAdmin.Services.DocumentAi
{
    // De nagebootste lezer: een vaste, voorspelbare uitkomst zonder netwerk, alleen voor tests en de rondleiding (DocumentAi:Reader = Demo).
    // De rondleiding geeft leverancier en ordernummer mee in de bestandsnaam ("demo-factuur--DEMO-TOUR-007 Kwekerij--DEMO-TOUR-005.png");
    // zonder die vorm een vaste demofactuur. Eén regel avocado's en een transportregel, met een twijfel bij de datum (gele markering).
    public sealed partial class DemoInvoiceAi : IDocumentAI
    {
        public const string Model = "demo-reader";

        [GeneratedRegex(@"--(?<vendor>[^-][^/\\]*?)--(?<order>[^/\\]+?)(\.[a-z0-9]+)?$", RegexOptions.IgnoreCase)]
        private static partial Regex Hints();

        public Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
        {
            var match = Hints().Match(document.Name ?? "");
            var vendor = match.Success ? match.Groups["vendor"].Value.Trim() : "Demo Leverancier BV";
            var order = match.Success ? match.Groups["order"].Value.Trim() : null;
            var json = new JsonObject
            {
                ["soort"] = "inkoopfactuur",
                ["leverancier"] = vendor,
                ["ordernummer_leverancier"] = order,
                ["factuurnummer"] = order == null ? "DEMO-F-1001" : $"AI-{order}",
                ["datum"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["valuta"] = "EUR",
                ["regels"] = new JsonArray(
                    Line("Avocado Hass 4 kg", 10m, "doos", 12.50m, 9m),
                    Line("Transport", 1m, null, 35.00m, 21m)),
                ["totaal_excl"] = 160.00m,
                ["btw_totaal"] = 18.60m,
                ["totaal_incl"] = 178.60m,
                ["leverancier_btw"] = "",
                ["leverancier_kvk"] = "",
                ["leverancier_iban"] = "",
                ["zekerheden"] = new JsonArray(new JsonObject { ["veld"] = "datum", ["zekerheid"] = "laag", ["plek"] = "pagina 1, rechtsboven" }),
            };
            return Task.FromResult(new AiResponse(json.ToJsonString(), 1200, 300, TimeSpan.FromMilliseconds(5), Model));
        }

        private static JsonObject Line(string description, decimal quantity, string unit, decimal price, decimal vat) => new()
        {
            ["omschrijving"] = description, ["aantal"] = quantity, ["eenheid"] = unit, ["kg_netto"] = null, ["kg_bruto"] = null,
            ["prijs"] = price, ["bedrag"] = quantity * price, ["btw_percentage"] = vat,
        };
    }
}
