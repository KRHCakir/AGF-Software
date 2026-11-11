using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

public class SchemaTests
{
    private static string Doc(string totaalIncl = "null", string regels = "[]", string zekerheden = "[]", string extra = "") =>
        """{"soort":"bon","leverancier":null,"ordernummer_leverancier":null,"factuurnummer":null,"datum":null,"valuta":null,"regels":""" + regels
        + ""","totaal_excl":null,"btw_totaal":null,"totaal_incl":""" + totaalIncl + ""","zekerheden":""" + zekerheden + extra + "}";

    private static string Line(string aantal) =>
        """{"omschrijving":null,"aantal":""" + aantal + ""","eenheid":null,"kg_netto":null,"kg_bruto":null,"prijs":null,"bedrag":null,"btw_percentage":null}""";

    [Theory]
    [InlineData("not json", "geen geldige JSON")]
    [InlineData("[1,2]", "geen JSON-object")]
    [InlineData("""{"soort":"brief"}""", "soort")]
    [InlineData("""{"soort":"bon"}""", "ontbreekt")]
    public void Invalid_output_is_rejected_with_a_reason(string text, string reason)
    {
        Assert.Null(ExtractionSchema.Validate(text, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Output_that_breaks_the_schema_is_rejected()
    {
        (string Text, string Reason)[] cases =
        [
            (Doc(totaalIncl: "\"12,50\""), "totaal_incl"),
            (Doc(regels: "null"), "regels"),
            (Doc(regels: "[" + Line("\"tien\"") + "]"), "regels[0].aantal"),
            (Doc(regels: """[{"aantal":1}]"""), "regels[0]."),
            (Doc(regels: "[null]"), "regels[0]"),
            (Doc(extra: ""","onzin":1"""), "onzin"),
            (Doc(zekerheden: "null"), "zekerheden"),
            (Doc(zekerheden: """[{"veld":"x","zekerheid":"zeker","plek":null}]"""), "zekerheid"),
            (Doc(zekerheden: """[{"veld":"x","zekerheid":"hoog"}]"""), "plek"),
        ];
        foreach (var (text, reason) in cases)
        {
            Assert.Null(ExtractionSchema.Validate(text, out var error));
            Assert.Contains(reason, error);
        }
    }

    [Fact]
    public void Complete_output_passes()
    {
        var text = Doc(regels: "[" + Line("2.5") + "]", totaalIncl: "12.5", zekerheden: """[{"veld":"x","zekerheid":"hoog","plek":null}]""");
        Assert.NotNull(ExtractionSchema.Validate(text, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void The_schema_stays_within_the_anthropic_limit_for_nullable_fields()
    {
        static int Unions(JsonNode? node) => node switch
        {
            JsonObject o => (o["type"] is JsonArray ? 1 : 0) + o.Sum(p => Unions(p.Value)),
            JsonArray a => a.Sum(Unions),
            _ => 0,
        };
        Assert.InRange(Unions(ExtractionSchema.Build()), 1, ExtractionSchema.MaxNullableFields);
    }

    [Fact]
    public void Valid_output_passes_also_inside_a_code_fence_and_the_schema_requires_every_field()
    {
        Assert.NotNull(ExtractionSchema.Validate("```json\n" + Doc() + "\n```", out _));
        var schema = ExtractionSchema.Build();
        Assert.Equal(11, ((JsonArray)schema["required"]!).Count);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }
}
