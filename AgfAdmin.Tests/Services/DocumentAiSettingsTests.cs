using AgfAdmin.Services.DocumentAi;
using Microsoft.Extensions.Configuration;

namespace AgfAdmin.Tests.Services;

// Besluit van de eigenaar (12 november): alleen Haiku leest, geen vangnet en geen ander model, in de standaardinstellingen en in
// appsettings.json.
public class DocumentAiSettingsTests
{
    [Fact]
    public void Only_Haiku_Reads_Without_A_Fallback_Or_Any_Other_Model()
    {
        var defaults = new DocumentAiOptions();
        Assert.Equal(("claude-haiku-5-5", false, null), (defaults.PrimaryModel, defaults.FallbackEnabled, defaults.FallbackModel));

        var configured = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build()
            .GetSection(DocumentAiOptions.Section).Get<DocumentAiOptions>();
        Assert.Equal(("claude-haiku-5-5", false), (configured.PrimaryModel, configured.FallbackEnabled));
        Assert.True(string.IsNullOrEmpty(configured.FallbackModel));
        Assert.Equal(new[] { "claude-haiku-5-5" }, configured.Models.Keys.ToArray());
    }
}
