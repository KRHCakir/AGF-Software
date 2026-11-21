using System.Globalization;
using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAdmin.Services.DocumentAi
{
    // De nagebootste lezer voor certificaten (certificaten deel 2): een vaste, voorspelbare uitkomst zonder netwerk, alleen voor tests en de
    // rondleiding (DocumentAi:Reader = Demo). Het voorbeeld van de eigenaar: een GLOBALG.A.P.-certificaat met Lettuce op pagina 1 en in de
    // bijlage (Annex) op pagina 2 Arugula en Mizuna, één productiesite, en een twijfel bij de einddatum (gele markering). De houder komt uit
    // de bestandsnaam ("demo-certificaat--DEMO-TOUR Kwekerij.pdf"), anders een vaste verzonnen naam. Alle gegevens zijn verzonnen.
    public sealed class DemoCertificateAi : IDocumentAI
    {
        public const string Number = "4063061591012";
        public const string DefaultHolder = "Kwekerij Verde Demo S.L.";

        public Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
        {
            var name = Path.GetFileNameWithoutExtension(document.Name ?? "");
            var hint = name.IndexOf("--", StringComparison.Ordinal);
            var holder = hint >= 0 && name.Length > hint + 2 ? name[(hint + 2)..].Trim() : DefaultHolder;
            var today = DateTime.UtcNow.Date;
            static JsonObject Product(string text, int page) => new() { ["tekst"] = text, ["pagina"] = page };
            var json = new JsonObject
            {
                ["soort"] = CertificateRules.GlobalGapIfa,
                ["soort_naam"] = "GLOBALG.A.P. Integrated Farm Assurance (IFA) – Fruit and Vegetables",
                ["nummer"] = Number,
                ["houder_naam"] = holder,
                ["houder_land"] = "ES",
                ["certificeringsinstantie"] = "Demo Certificering B.V.",
                ["geldig_van"] = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["geldig_tot"] = today.AddDays(335).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["producten"] = new JsonArray(Product("Lettuce", 1), Product("Arugula", 2), Product("Mizuna", 2)),
                ["productiesites"] = new JsonArray(new JsonObject { ["naam"] = "Finca Demo Norte", ["land"] = "ES" }),
                ["zekerheden"] = new JsonArray(new JsonObject { ["veld"] = "geldig_tot", ["zekerheid"] = "laag", ["plek"] = "pagina 1, rechtsonder" }),
            };
            return Task.FromResult(new AiResponse(json.ToJsonString(), 2400, 350, TimeSpan.FromMilliseconds(5), DemoInvoiceAi.Model));
        }
    }
}
