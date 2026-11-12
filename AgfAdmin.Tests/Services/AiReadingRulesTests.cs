using System.Text.Json.Nodes;
using AgfAdmin.Services.DocumentAi;
using AgfDocumentAi;

namespace AgfAdmin.Tests.Services;

// De controles op een AI-voorstel (welke het vangnet laten lezen), kosten, eenheden en het schema met de leverancierskenmerken.
public class AiReadingRulesTests
{
    public static JsonObject Proposal(Action<JsonObject> change = null)
    {
        var proposal = new JsonObject
        {
            ["soort"] = "inkoopfactuur", ["leverancier"] = "Fruitimport Zuid B.V.", ["ordernummer_leverancier"] = "45-21", ["factuurnummer"] = "F-2026-118",
            ["datum"] = "2026-11-10", ["valuta"] = "EUR",
            ["regels"] = new JsonArray(
                new JsonObject { ["omschrijving"] = "Avocado Hass 4 kg", ["aantal"] = 10m, ["eenheid"] = "dozen", ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = 12.50m, ["bedrag"] = 125.00m, ["btw_percentage"] = 9m },
                new JsonObject { ["omschrijving"] = "Transport", ["aantal"] = 1m, ["eenheid"] = null, ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = 35.00m, ["bedrag"] = 35.00m, ["btw_percentage"] = 21m }),
            ["totaal_excl"] = 160.00m, ["btw_totaal"] = 18.60m, ["totaal_incl"] = 178.60m,
            ["leverancier_btw"] = "NL001234567B01", ["leverancier_kvk"] = "12345678", ["leverancier_iban"] = "NL91ABNA0417164300",
            ["zekerheden"] = new JsonArray(),
        };
        change?.Invoke(proposal);
        return proposal;
    }

    [Fact]
    public void A_consistent_invoice_has_no_problems()
    {
        Assert.Empty(AiReadingRules.Problems(Proposal()));
    }

    [Fact]
    public void Totals_that_do_not_add_up_trigger_the_fallback_and_are_marked_on_the_total()
    {
        var problems = AiReadingRules.Problems(Proposal(p => p["totaal_excl"] = 150.00m));
        Assert.Contains(problems, p => p is { Field: "totalNet", Code: AiReadingRules.LinesMismatch, TriggersFallback: true });
        Assert.Contains(problems, p => p is { Field: "totalGross", Code: AiReadingRules.GrossMismatch });
        Assert.True(AiReadingRules.NeedsFallback(problems));

        var vat = AiReadingRules.Problems(Proposal(p => { p["btw_totaal"] = 25.00m; p["totaal_incl"] = 185.00m; }));
        Assert.Equal(new[] { ("totalVat", AiReadingRules.VatMismatch) }, vat.Select(p => (p.Field, p.Code)));
    }

    [Fact]
    public void A_missing_required_field_triggers_the_fallback_a_missing_optional_one_is_only_marked()
    {
        var missing = AiReadingRules.Problems(Proposal(p => { p["factuurnummer"] = null; p["valuta"] = null; }));
        Assert.Contains(missing, p => p is { Field: "invoiceNumber", Code: AiReadingRules.Missing, TriggersFallback: true });
        Assert.Contains(missing, p => p is { Field: "currency", Code: AiReadingRules.Missing, TriggersFallback: false });
        Assert.Contains(AiReadingRules.Problems(Proposal(p => p["datum"] = "10-11-2026")), p => p is { Field: "invoiceDate", Code: AiReadingRules.InvalidDate, TriggersFallback: true });
        Assert.Contains(AiReadingRules.Problems(Proposal(p => p["regels"] = new JsonArray())), p => p is { Field: "lines", Code: AiReadingRules.Missing, TriggersFallback: true });
    }

    [Fact]
    public void A_missing_vat_total_is_marked_without_the_fallback()
    {
        var problems = AiReadingRules.Problems(Proposal(p => p["btw_totaal"] = null));
        Assert.Equal(new[] { ("totalVat", AiReadingRules.Missing) }, problems.Select(p => (p.Field, p.Code)));
        Assert.False(AiReadingRules.NeedsFallback(problems));
    }

    [Fact]
    public void Low_certainty_is_marked_on_the_form_field_and_only_triggers_the_fallback_for_a_required_field()
    {
        var problems = AiReadingRules.Problems(Proposal(p => p["zekerheden"] = new JsonArray(
            new JsonObject { ["veld"] = "datum", ["zekerheid"] = "laag", ["plek"] = "" },
            new JsonObject { ["veld"] = "regels[1].omschrijving", ["zekerheid"] = "laag", ["plek"] = "" },
            new JsonObject { ["veld"] = "factuurnummer", ["zekerheid"] = "hoog", ["plek"] = "" })));
        Assert.Equal(new[] { ("invoiceDate", true), ("lines[1].description", false) }, problems.Select(p => (p.Field, p.TriggersFallback)));
    }

    [Fact]
    public void A_line_amount_that_is_not_quantity_times_price_is_only_marked_a_discount_can_explain_it()
    {
        var problems = AiReadingRules.Problems(Proposal(p =>
        {
            var line = (JsonObject)p["regels"]![0]!;
            line["bedrag"] = 118.75m;
            p["totaal_excl"] = 153.75m; p["btw_totaal"] = 18.04m; p["totaal_incl"] = 171.79m;
        }));
        Assert.Equal(new[] { ("lines[0].amount", AiReadingRules.LineMismatch, false) }, problems.Select(p => (p.Field, p.Code, p.TriggersFallback)));
    }

    [Fact]
    public void Cost_uses_the_tier_above_the_input_threshold_and_converts_to_euro()
    {
        var haiku = new DocumentAiModelPrice { InputPerMillionUsd = 0.10m, OutputPerMillionUsd = 0.50m, TierAboveInputTokens = 100_000, TierInputPerMillionUsd = 0.50m, TierOutputPerMillionUsd = 2.50m };
        Assert.Equal(0.00095m, AiReadingRules.CostUsd(haiku, 4_500, 1_000));
        Assert.Equal(0.0625m, AiReadingRules.CostUsd(haiku, 120_000, 1_000));
        Assert.Equal((0.50m, 2.50m), AiReadingRules.Rates(haiku, 120_000));
        Assert.Equal((0.10m, 0.50m), AiReadingRules.Rates(haiku, 100_000));
        Assert.Equal(0.000817m, AiReadingRules.ToEur(0.00095m, 0.86m));
    }

    [Theory]
    [InlineData("dozen", "box")]
    [InlineData("Colli", "colli")]
    [InlineData("stuks", "piece")]
    [InlineData("kg", null)]
    [InlineData(null, null)]
    public void Units_on_the_invoice_map_to_a_line_unit_or_stay_empty(string text, string unit) => Assert.Equal(unit, AiReadingRules.Unit(text));

    [Fact]
    public void Schema_for_the_invoice_reader_adds_supplier_ids_as_plain_text_within_the_nullable_limit()
    {
        var schema = ExtractionSchema.Build(supplierIds: true);
        var properties = (JsonObject)schema["properties"]!;
        Assert.All(ExtractionSchema.SupplierIdFields, f => Assert.Equal("string", properties[f]!["type"]!.GetValue<string>()));
        Assert.True(schema.ToJsonString().Split("\"null\"").Length - 1 <= ExtractionSchema.MaxNullableFields);
        Assert.Equal(ExtractionSchema.Build().ToJsonString(), ExtractionSchema.Build(supplierIds: false).ToJsonString());

        Assert.NotNull(ExtractionSchema.Validate(Proposal().ToJsonString(), supplierIds: true, out _));
        Assert.Null(ExtractionSchema.Validate(Proposal(p => p.Remove("leverancier_iban")).ToJsonString(), supplierIds: true, out var error));
        Assert.Contains("leverancier_iban", error);
    }

    [Fact]
    public void The_demo_reader_takes_supplier_and_order_from_the_tour_file_name_and_needs_no_network()
    {
        var response = new DemoInvoiceAi().ExtractAsync(new DocumentInput("demo-factuur--DEMO-TOUR-007 Kwekerij--DEMO-TOUR-005.png", new byte[] { 1 }, "image/png"), "", default).Result;
        var proposal = ExtractionSchema.Validate(response.Text, supplierIds: true, out _)!;
        Assert.Equal("DEMO-TOUR-007 Kwekerij", proposal["leverancier"]!.GetValue<string>());
        Assert.Equal("DEMO-TOUR-005", proposal["ordernummer_leverancier"]!.GetValue<string>());
        Assert.Equal(new[] { ("invoiceDate", AiReadingRules.Uncertain) }, AiReadingRules.Problems(proposal).Select(p => (p.Field, p.Code)));
    }
}
