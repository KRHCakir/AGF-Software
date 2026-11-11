using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

public class ComparerTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static FieldScore Field(List<FieldScore> scores, string path) => scores.Single(s => s.Path == path);

    [Theory]
    [InlineData("04-11-2026")]
    [InlineData("4-11-2026")]
    [InlineData("04/11/2026")]
    [InlineData("04.11.2026")]
    [InlineData("2026-11-04")]
    [InlineData("2026-11-04T00:00:00")]
    public void Dates_are_normalized_to_year_month_day_with_day_first(string written) =>
        Assert.Equal("2026-11-04", Comparer.Normalize(FieldKind.Date, "datum", JsonValue.Create(written)));

    [Theory]
    [InlineData("1.250,00", 1250)]
    [InlineData("€ 12,5", 12.5)]
    [InlineData("1.250", 1250)]
    [InlineData("12.50", 12.5)]
    [InlineData("-3,10", -3.1)]
    public void Numbers_in_nl_notation_are_read(string written, decimal expected) =>
        Assert.Equal(expected, Comparer.Number(JsonValue.Create(written)));

    [Fact]
    public void Amounts_compare_on_cents_and_numbers_ignore_case_and_spaces()
    {
        var truth = Obj("""{"soort":"inkoopfactuur","factuurnummer":"VFZ-2026-0142","totaal_excl":"1.725,00","btw_totaal":"165,45","datum":"04-11-2026"}""");
        var output = Obj("""{"soort":"inkoopfactuur","factuurnummer":" vfz-2026-0142 ","totaal_excl":1725,"btw_totaal":165.454,"datum":"2026-11-04"}""");
        var scores = Comparer.Compare(truth, output);
        Assert.All(new[] { "factuurnummer", "totaal_excl", "btw_totaal", "datum" }, p => Assert.Equal(Outcome.Goed, Field(scores, p).Outcome));
        Assert.Equal(Outcome.Fout, Field(Comparer.Compare(truth, Obj("""{"btw_totaal":165.46}""")), "btw_totaal").Outcome);
    }

    [Fact]
    public void Missing_field_is_ontbreekt_and_a_value_where_the_truth_is_null_is_verzonnen()
    {
        var scores = Comparer.Compare(Obj("""{"soort":"bon","leverancier":"Frutas de Ejemplo S.L.","valuta":null}"""),
            Obj("""{"soort":"bon","leverancier":null,"valuta":"EUR"}"""));
        Assert.Equal(Outcome.Ontbreekt, Field(scores, "leverancier").Outcome);
        Assert.Equal(Outcome.Verzonnen, Field(scores, "valuta").Outcome);
        Assert.Equal(Outcome.Goed, Field(scores, "factuurnummer").Outcome);
    }

    [Fact]
    public void Lines_are_matched_on_description_first_and_the_rest_by_order()
    {
        var truth = Obj("""{"regels":[{"omschrijving":"Avocado Hass Peru","bedrag":10},{"omschrijving":"Mango Kent","bedrag":20},{"omschrijving":"Transport","bedrag":5}]}""");
        var output = Obj("""{"regels":[{"omschrijving":"MANGO kent","bedrag":20},{"omschrijving":"Vracht","bedrag":5},{"omschrijving":"Avocado Hass (Peru)","bedrag":10},{"omschrijving":"Pallet","bedrag":1}]}""");
        Assert.Equal([(0, 2), (1, 0), (2, 1)], Comparer.MatchLines([.. ((JsonArray)truth["regels"]!).OfType<JsonObject>()], [.. ((JsonArray)output["regels"]!).OfType<JsonObject>()]));
        var scores = Comparer.Compare(truth, output);
        Assert.Equal(Outcome.Goed, Field(scores, "regels[0].bedrag").Outcome);
        Assert.Equal(Outcome.Fout, Field(scores, "regels[2].omschrijving").Outcome);
        Assert.Equal(Outcome.Verzonnen, Field(scores, "extra regel 4").Outcome);
    }

    [Fact]
    public void A_forgotten_line_counts_every_field_of_it_and_high_certainty_is_tracked_on_the_models_own_index()
    {
        var truth = Obj("""{"regels":[{"omschrijving":"Limon","aantal":80},{"omschrijving":"Naranja","aantal":60}]}""");
        var output = Obj("""{"regels":[{"omschrijving":"Naranja","aantal":61}],"zekerheden":[{"veld":"regels[0].aantal","zekerheid":"hoog","plek":null}]}""");
        var scores = Comparer.Compare(truth, output);
        Assert.Equal(Outcome.Ontbreekt, Field(scores, "regels[0].aantal").Outcome);
        Assert.True(Field(scores, "regels[1].aantal") is { Outcome: Outcome.Fout, HighCertainty: true });
    }
}
