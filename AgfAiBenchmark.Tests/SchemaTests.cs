using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

public class SchemaTests
{
    [Theory]
    [InlineData("not json", "geen geldige JSON")]
    [InlineData("[1,2]", "geen JSON-object")]
    [InlineData("""{"soort":"brief"}""", "soort")]
    [InlineData("""{"soort":"bon","totaal_incl":"12,50"}""", "totaal_incl")]
    [InlineData("""{"soort":"bon","regels":[{"aantal":"tien"}]}""", "regels[0].aantal")]
    public void Invalid_output_is_rejected_with_a_reason(string text, string reason)
    {
        Assert.Null(ExtractionSchema.Validate(text, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Valid_output_passes_also_inside_a_code_fence_and_the_schema_requires_every_field()
    {
        Assert.NotNull(ExtractionSchema.Validate("```json\n{\"soort\":\"bon\",\"regels\":[]}\n```", out _));
        var schema = ExtractionSchema.Build();
        Assert.Equal(11, ((JsonArray)schema["required"]!).Count);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }
}
