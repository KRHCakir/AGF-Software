using System.Text.Json.Nodes;
using AgfAdmin.Services.DocumentAi;
using AgfDocumentAi;

namespace AgfAdmin.Tests.Services;

// De controles op een AI-voorstel (welke het vangnet laten lezen), kosten, eenheden, kortingen en het schema met de leverancierskenmerken
// en kortingen.
public class AiReadingRulesTests
{
    public static JsonObject Proposal(Action<JsonObject> change = null)
    {
        var proposal = new JsonObject
        {
            ["soort"] = "inkoopfactuur", ["leverancier"] = "Fruitimport Zuid B.V.", ["ordernummer_leverancier"] = "45-21", ["factuurnummer"] = "F-2026-118",
            ["datum"] = "2026-11-10", ["valuta"] = "EUR",
            ["regels"] = new JsonArray(
                new JsonObject { ["omschrijving"] = "Avocado Hass 4 kg", ["aantal"] = 10m, ["eenheid"] = "dozen", ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = 12.50m, ["bedrag"] = 125.00m, ["btw_percentage"] = 9m, ["korting_percentage"] = 0m },
                new JsonObject { ["omschrijving"] = "Transport", ["aantal"] = 1m, ["eenheid"] = null, ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = 35.00m, ["bedrag"] = 35.00m, ["btw_percentage"] = 21m, ["korting_percentage"] = 0m }),
            ["totaal_excl"] = 160.00m, ["btw_totaal"] = 18.60m, ["totaal_incl"] = 178.60m, ["korting_totaal"] = 0m,
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
    public void Schema_for_the_invoice_reader_adds_supplier_ids_and_discounts_as_plain_fields_within_the_nullable_limit()
    {
        var schema = ExtractionSchema.Build(SchemaParts.Admin);
        var properties = (JsonObject)schema["properties"]!;
        Assert.All(ExtractionSchema.SupplierIdFields, f => Assert.Equal("string", properties[f]!["type"]!.GetValue<string>()));
        Assert.Equal("number", properties[ExtractionSchema.DocumentDiscountField]!["type"]!.GetValue<string>());
        Assert.Equal("number", properties["regels"]!["items"]!["properties"]![ExtractionSchema.LineDiscountField]!["type"]!.GetValue<string>());
        Assert.True(schema.ToJsonString().Split("\"null\"").Length - 1 <= ExtractionSchema.MaxNullableFields);
        // De testbench houdt zijn schema (en daarmee zijn vingerafdruk): zonder leverancierskenmerken en zonder kortingen.
        Assert.Equal(ExtractionSchema.Build().ToJsonString(), ExtractionSchema.Build(SchemaParts.Base).ToJsonString());
        Assert.DoesNotContain("korting_", ExtractionSchema.Build().ToJsonString());

        Assert.NotNull(ExtractionSchema.Validate(Proposal().ToJsonString(), SchemaParts.Admin, out _));
        Assert.Null(ExtractionSchema.Validate(Proposal(p => p.Remove("leverancier_iban")).ToJsonString(), SchemaParts.Admin, out var error));
        Assert.Contains("leverancier_iban", error);
        Assert.Null(ExtractionSchema.Validate(Proposal(p => p["korting_totaal"] = null).ToJsonString(), SchemaParts.Admin, out error));
        Assert.Contains("korting_totaal", error);
        Assert.Null(ExtractionSchema.Validate(Proposal(p => p["regels"]![0]!.AsObject().Remove("korting_percentage")).ToJsonString(), SchemaParts.Admin, out error));
        Assert.Contains("korting_percentage", error);
    }

    // De factuur van De Westlandse Tuin (testfactuur 2) zoals Claude Haiku 5.5 hem las: 5% korting op de komkommers en 3% op het totaal.
    private static JsonObject Westlandse(decimal cucumberDiscount, decimal documentDiscount)
    {
        JsonObject Line(string description, decimal quantity, decimal price, decimal amount, decimal vat, decimal discount = 0m) => new()
        {
            ["omschrijving"] = description, ["aantal"] = quantity, ["eenheid"] = "kist", ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = price, ["bedrag"] = amount,
            ["btw_percentage"] = vat, ["korting_percentage"] = discount,
        };
        return Proposal(p =>
        {
            p["regels"] = new JsonArray(
                Line("Tomaten trostomaat 6+ kist 6 kg", 200m, 8.90m, 1780.00m, 9m), Line("Paprika mix 5 kg", 150m, 12.35m, 1852.50m, 9m),
                Line("Komkommers 12 stuks per kist", 180m, 7.60m, 1299.60m, 9m, cucumberDiscount), Line("Aubergine 5 kg", 40m, 10.15m, 406.00m, 9m),
                Line("Huur plastic kratten", 570m, 0.45m, 256.50m, 21m), Line("Transportkosten", 1m, 145.00m, 145.00m, 21m));
            p["totaal_excl"] = 5567.41m;
            p["btw_totaal"] = 547.81m;
            p["totaal_incl"] = 6115.22m;
            p["korting_totaal"] = documentDiscount;
        });
    }

    [Fact]
    public void Without_the_discounts_the_invoice_does_not_add_up_with_them_it_does()
    {
        // Zo las Haiku de factuur vóór deze wijziging: alle bedragen goed, maar zonder kortingen rekenen regels en totaal niet.
        Assert.Equal(new[] { ("lines[2].amount", AiReadingRules.LineMismatch), ("totalNet", AiReadingRules.LinesMismatch), ("totalVat", AiReadingRules.VatMismatch) },
            AiReadingRules.Problems(Westlandse(0m, 0m)).Select(p => (p.Field, p.Code)));
        Assert.Empty(AiReadingRules.Problems(Westlandse(5m, 172.19m)));
    }

    [Fact]
    public void A_clean_discount_on_the_total_goes_into_every_line_discount_of_the_form()
    {
        var (lines, percentage) = AiReadingRules.FormDiscounts(Westlandse(5m, 172.19m));
        Assert.Equal(3m, percentage);
        Assert.Equal(new[] { 3m, 3m, 7.85m, 3m, 3m, 3m }, lines);

        var proposal = AiInvoiceReadingService.ToProposal(Westlandse(5m, 172.19m));
        Assert.Equal((172.19m, 3m), (proposal.DocumentDiscount, proposal.DocumentDiscountPercentage));
        Assert.Equal(7.85m, proposal.Lines[2].DiscountPercentage);
        // Het formulier rekent dan op de cent na hetzelfde totaal als de factuur (per regel afgerond).
        var net = proposal.Lines.Sum(l => AgfAdmin.Services.InvoiceMath.LineNet(l.Quantity!.Value, l.UnitPrice!.Value, l.DiscountPercentage));
        Assert.InRange(net, 5567.41m - 0.02m, 5567.41m + 0.02m);

        // Alleen een korting op de regel: die gaat zoals gelezen naar het formulier, er is geen korting op het totaal.
        var lineOnly = AiInvoiceReadingService.ToProposal(Proposal(p => { p["regels"]![0]!["korting_percentage"] = 5m; p["regels"]![0]!["bedrag"] = 118.75m; p["totaal_excl"] = 153.75m; }));
        Assert.Equal((5m, 0m, (decimal?)null, (decimal?)null), (lineOnly.Lines[0].DiscountPercentage, lineOnly.Lines[1].DiscountPercentage, lineOnly.DocumentDiscount, lineOnly.DocumentDiscountPercentage));
    }

    [Fact]
    public void A_discount_on_the_total_that_cannot_be_spread_exactly_stays_with_the_user_and_is_marked_without_the_fallback()
    {
        // Een vast bedrag van 50,00 op 5.739,60 is 0,87...%: geen percentage met twee decimalen, dus niet over de regels te verdelen.
        var fixedAmount = Westlandse(5m, 50.00m);
        fixedAmount["totaal_excl"] = 5689.60m;
        fixedAmount["btw_totaal"] = 559.82m;
        fixedAmount["totaal_incl"] = 6249.42m;
        var problems = AiReadingRules.Problems(fixedAmount);
        Assert.Equal(new[] { ("documentDiscount", AiReadingRules.DiscountNotSpread) }, problems.Select(p => (p.Field, p.Code)));
        Assert.False(AiReadingRules.NeedsFallback(problems));
        var proposal = AiInvoiceReadingService.ToProposal(fixedAmount);
        Assert.Equal((50.00m, (decimal?)null, 5m, 0m), (proposal.DocumentDiscount, proposal.DocumentDiscountPercentage, proposal.Lines[2].DiscountPercentage, proposal.Lines[0].DiscountPercentage));

        // 2,5% op het totaal over 5% op de regel is 7,375%: niet in twee decimalen, dus ook niet verdeeld.
        var (lines, percentage) = AiReadingRules.FormDiscounts(Westlandse(5m, 143.49m));
        Assert.Null(percentage);
        Assert.Equal(5m, lines[2]);
    }

    [Fact]
    public void A_discount_on_the_total_is_not_spread_when_the_rounded_form_lines_miss_the_invoice_total()
    {
        // Twintig regels van 1,05 met 10% korting: factuur 18,90, maar het formulier rondt elke regel 0,945 af op 0,95 en komt op 19,00.
        var proposal = Proposal(p =>
        {
            p["regels"] = new JsonArray(Enumerable.Range(0, 20).Select(_ => (JsonNode)new JsonObject
            {
                ["omschrijving"] = "Artikel", ["aantal"] = 1m, ["eenheid"] = "stuks", ["kg_netto"] = null, ["kg_bruto"] = null, ["prijs"] = 1.05m, ["bedrag"] = 1.05m,
                ["btw_percentage"] = 9m, ["korting_percentage"] = 0m,
            }).ToArray());
            p["korting_totaal"] = 2.10m;
            p["totaal_excl"] = 18.90m;
            p["btw_totaal"] = 1.70m;
            p["totaal_incl"] = 20.60m;
        });
        var (lines, percentage) = AiReadingRules.FormDiscounts(proposal);
        Assert.Null(percentage);
        Assert.All(lines, d => Assert.Equal(0m, d));
        Assert.Contains(AiReadingRules.Problems(proposal), p => p is { Field: "documentDiscount", Code: AiReadingRules.DiscountNotSpread, TriggersFallback: false });
    }

    [Fact]
    public void A_free_line_with_100_percent_discount_adds_up_and_goes_into_the_form()
    {
        var proposal = Proposal(p => { p["regels"]![1]!["korting_percentage"] = 100m; p["regels"]![1]!["bedrag"] = 0m; p["totaal_excl"] = 125.00m; p["btw_totaal"] = 11.25m; p["totaal_incl"] = 136.25m; });
        Assert.Empty(AiReadingRules.Problems(proposal));
        Assert.Equal(100m, AiInvoiceReadingService.ToProposal(proposal).Lines[1].DiscountPercentage);
    }

    [Fact]
    public void A_discount_the_AI_doubts_is_marked_on_the_discount_field()
    {
        Assert.Equal("lines[2].discountPercentage", AiReadingRules.FormField("regels[2].korting_percentage"));
        Assert.Equal("documentDiscount", AiReadingRules.FormField("korting_totaal"));
    }

    [Fact]
    public void The_demo_reader_takes_supplier_and_order_from_the_tour_file_name_and_needs_no_network()
    {
        var response = new DemoInvoiceAi().ExtractAsync(new DocumentInput("demo-factuur--DEMO-TOUR-007 Kwekerij--DEMO-TOUR-005.png", new byte[] { 1 }, "image/png"), "", default).Result;
        var proposal = ExtractionSchema.Validate(response.Text, SchemaParts.Admin, out _)!;
        Assert.Equal("DEMO-TOUR-007 Kwekerij", proposal["leverancier"]!.GetValue<string>());
        Assert.Equal("DEMO-TOUR-005", proposal["ordernummer_leverancier"]!.GetValue<string>());
        Assert.Equal(new[] { ("invoiceDate", AiReadingRules.Uncertain) }, AiReadingRules.Problems(proposal).Select(p => (p.Field, p.Code)));
    }
}
