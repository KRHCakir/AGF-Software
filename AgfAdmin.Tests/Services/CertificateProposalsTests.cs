using System.Text.Json.Nodes;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfDocumentAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 2, de regels zonder database: de uitvoer van de AI (het vaste schema), de nagebootste lezer met het voorbeeld van de
// eigenaar (Lettuce op pagina 1, Arugula en Mizuna in de bijlage op pagina 2) en het voorstel op het scherm: soort, nummer met de
// GGN-controle, datums met de status, gele twijfel, "niet gevonden", de koppeling van producten (nooit een nieuw product), een houder die
// duidelijk een ander bedrijf is en een certificaat met hetzelfde nummer.
public class CertificateProposalsTests
{
    private const string Holder = "Kwekerij Verde Demo S.L.";
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly CertificateProposals.OwnProduct[] Products = { new(10, "Sla"), new(11, "Rucola") };

    // Het voorbeeld van de eigenaar, zoals de nagebootste lezer het geeft (vaste datums voor de test).
    public static JsonObject Example(string holder = Holder, string number = DemoCertificateAi.Number)
    {
        var json = (JsonObject)JsonNode.Parse(new DemoCertificateAi().ExtractAsync(new DocumentInput("c.pdf", Pdf, "application/pdf"), "", default).Result.Text)!;
        json["houder_naam"] = holder;
        json["nummer"] = number;
        json["geldig_van"] = "2026-03-01";
        json["geldig_tot"] = "2026-12-10";
        return json;
    }

    private static AiCertificateProposalDto Build(JsonObject json, out List<AiProblemDto> problems, string owner = Holder, params CertificateProposals.StoredCertificate[] stored) =>
        CertificateProposals.Build(json, Products, stored, new[] { owner }, Today, 30, out problems);

    [Fact]
    public void The_example_gives_the_products_of_page_1_and_the_annex_with_doubt_on_the_end_date_and_the_status()
    {
        var p = Build(Example(), out var problems);
        Assert.Equal((CertificateRules.GlobalGapIfa, DemoCertificateAi.Number, Holder, "ES", "Demo Certificering B.V."), (p.TypeCode, p.Number, p.HolderName, p.HolderCountry, p.CertificationBody));
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 10), CertificateRules.StatusExpiringSoon, 19), (p.ValidFrom!.Value, p.ValidUntil!.Value, p.Status, p.DaysLeft!.Value));
        Assert.Equal(new[] { ("Lettuce", 1, (int?)10, "Translated"), ("Arugula", 2, 11, "Translated"), ("Mizuna", 2, null, "None") },
            p.Products.Select(x => (x.Text, x.Page!.Value, x.ProductId, x.Match)));
        Assert.Equal(("Finca Demo Norte", "ES"), (p.Sites.Single().Name, p.Sites.Single().Country));
        Assert.Contains(problems, x => (x.Field, x.Code) == ("validUntil", AiReadingRules.Uncertain));
        Assert.Contains(problems, x => (x.Field, x.Code) == ("products[2]", CertificateProposals.ProblemUnmatched));
        Assert.Null(p.HolderMismatch);
        Assert.Null(p.SameNumberCertificateId);
    }

    [Fact]
    public void A_wrong_ggn_an_unreadable_date_or_a_missing_field_is_marked_and_nothing_is_invented()
    {
        var json = Example();
        json["nummer"] = "40630615910";
        json["geldig_van"] = "1 maart 2026";
        json["certificeringsinstantie"] = "";
        var p = Build(json, out var problems);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("number", CertificateProposals.ProblemGgnInvalid));
        Assert.Contains(problems, x => (x.Field, x.Code) == ("validFrom", AiReadingRules.InvalidDate));
        Assert.Contains(problems, x => (x.Field, x.Code) == ("certificationBody", AiReadingRules.Missing));
        Assert.Equal(((DateOnly?)null, (string)null), (p.ValidFrom, p.CertificationBody));
        json["nummer"] = "4063061591013";
        Build(json, out problems);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("number", CertificateProposals.ProblemGgnCheckDigit));
        // Te lang: gemarkeerd, niet ingekort; meer producten dan het voorstel toont: gemeld.
        json["nummer"] = new string('7', CertificateRules.MaxNumberLength + 5);
        json["soort"] = "Skal";
        var longNumber = Build(json, out problems);
        Assert.Equal(CertificateRules.MaxNumberLength + 5, longNumber.Number.Length);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("number", CertificateProposals.ProblemTooLong));
        var many = Example();
        many["producten"] = new JsonArray(Enumerable.Range(1, CertificateProposals.MaxProducts + 3).Select(i => (JsonNode)new JsonObject { ["tekst"] = $"Product {i}", ["pagina"] = 1 }).ToArray());
        Assert.Equal(CertificateProposals.MaxProducts, Build(many, out problems).Products.Count);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("products", CertificateProposals.ProblemTruncated));
        // Een soort die niet in de lijst staat: een eigen soort met de naam uit het document.
        json["soort"] = CertificateSchema.Other;
        json["soort_naam"] = "Demeter biodynamisch";
        var custom = Build(json, out _);
        Assert.Equal((CertificateRules.Custom, "Demeter biodynamisch"), (custom.TypeCode, custom.CustomTypeName));
        // Een te lange naam blijft volledig en wordt gemarkeerd; meer productielocaties dan getoond wordt gemeld.
        json["soort_naam"] = new string('D', CertificateRules.MaxCustomTypeLength + 10);
        json["productiesites"] = new JsonArray(Enumerable.Range(1, CertificateProposals.MaxSites + 2).Select(i => (JsonNode)new JsonObject { ["naam"] = $"Finca {i}", ["land"] = "ES" }).ToArray());
        var longName = Build(json, out problems);
        Assert.Equal(CertificateRules.MaxCustomTypeLength + 10, longName.CustomTypeName.Length);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("typeCode", CertificateProposals.ProblemTooLong));
        Assert.Equal(CertificateProposals.MaxSites, longName.Sites.Count);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("sites", CertificateProposals.ProblemTruncated));
    }

    [Fact]
    public void Another_company_as_holder_warns_and_the_same_number_at_the_relation_proposes_a_new_version()
    {
        Assert.Null(Build(Example(), out _, "Kwekerij Verde Demo B.V.").HolderMismatch);
        var other = Build(Example("Agrícola Sol Naciente S.A."), out var problems);
        Assert.Equal("Agrícola Sol Naciente S.A.", other.HolderMismatch);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("holderName", CertificateProposals.ProblemHolder));
        Assert.True(CertificateProposals.SameCompany("Kwekerij Verde Demo S.L.", "kwekerij verde"));
        Assert.False(CertificateProposals.SameCompany("Agrícola Sol Naciente S.A.", "Fruithandel Noord B.V."));
        var renewed = Build(Example(), out _, Holder, new CertificateProposals.StoredCertificate(7, CertificateRules.GlobalGapIfa, null, "4063 0615 91012"));
        Assert.Equal((7, "GLOBALG.A.P. IFA (GGN)"), (renewed.SameNumberCertificateId!.Value, renewed.SameNumberTypeName));
        // Hetzelfde nummer op een ander soort certificaat (GRASP) is geen eerdere versie hiervan.
        Assert.Null(Build(Example(), out _, Holder, new CertificateProposals.StoredCertificate(8, "Grasp", null, "4063061591012")).SameNumberCertificateId);
    }

    [Fact]
    public async Task The_demo_reader_gives_the_example_with_the_annex_and_its_output_passes_the_schema()
    {
        var configuration = new ConfigurationBuilder().Build();
        var factory = new DocumentAiFactory(Options.Create(new DocumentAiOptions { Reader = DocumentAiOptions.ReaderDemo }), configuration);
        var response = await factory.CreateCertificateReader("x").ExtractAsync(new DocumentInput("demo-certificaat--Kwekerij Tour Demo.pdf", Pdf, "application/pdf"), CertificateSchema.Instruction, default);
        var json = CertificateSchema.Validate(response.Text, out var error);
        Assert.Null(error);
        Assert.Equal("Kwekerij Tour Demo", json!["houder_naam"]!.GetValue<string>());
        Assert.Equal(new[] { ("Lettuce", 1), ("Arugula", 2), ("Mizuna", 2) },
            json["producten"]!.AsArray().Select(p => (p!["tekst"]!.GetValue<string>(), p["pagina"]!.GetValue<int>())));
        Assert.True(CertificateRules.HasValidCheckDigit(DemoCertificateAi.Number));
        // Ongeldige uitvoer telt als fout (dan probeert het vangnet het, als dat aan staat).
        Assert.Null(CertificateSchema.Validate("{\"soort\":\"Onbekend\"}", out var bad));
        Assert.NotNull(bad);
        Assert.Contains("Annex", CertificateSchema.Instruction);
        Assert.Contains("Verzin niets", CertificateSchema.Instruction);
    }

    [Theory]
    [InlineData("Lettuce", "Sla", "Translated")]
    [InlineData("Rocket (Eruca sativa)", "Rucola", "Translated")]
    [InlineData("Iceberg lettuce", "IJsbergsla", "Translated")]
    [InlineData("Tomatoes", "Tomaat", "Translated")]
    [InlineData("rucola", "Rucola", "Exact")]
    [InlineData("Avocados Hass", "Avocado", "Translated")]
    [InlineData("Baby leaf Sla mix", "Sla", "Partial")]
    [InlineData("Mizuna", null, "None")]
    [InlineData("Lettuce and Tomaat", null, "None")]
    [InlineData("Tomaten", "Tomaat", "Exact")]
    [InlineData("Tomaten los (Solanum lycopersicum)", "Tomaat", "Partial")]
    [InlineData("Lettuce, Rucola", null, "None")]
    public void Products_are_linked_by_name_translation_or_a_word_and_never_created(string text, string expected, string match)
    {
        var products = new[] { new CertificateProposals.OwnProduct(1, "Sla"), new(2, "Rucola"), new(3, "IJsbergsla"), new(4, "Tomaat"), new(5, "Avocado") };
        var (product, kind) = CertificateProposals.MatchProduct(text, products);
        Assert.Equal((expected, match), (product?.Name, kind));
    }

    [Fact]
    public void A_separately_mentioned_product_is_kept_next_to_a_more_specific_name()
    {
        var products = new[] { new CertificateProposals.OwnProduct(4, "Tomaat"), new(6, "Cherrytomaat") };
        Assert.Equal(CertificateProposals.MatchNone, CertificateProposals.MatchProduct("Cherry tomato and tomato", products).Match);
        Assert.Equal(CertificateProposals.MatchNone, CertificateProposals.MatchProduct("Tomatoes, cherry tomatoes", products).Match);
    }

    [Fact]
    public void Several_products_in_one_text_or_a_missing_specific_product_are_not_linked()
    {
        var products = new[] { new CertificateProposals.OwnProduct(1, "Sla"), new(4, "Tomaat") };
        Assert.Equal(CertificateProposals.MatchNone, CertificateProposals.MatchProduct("Lettuce and tomatoes", products).Match);
        Assert.Null(CertificateProposals.MatchProduct("Cherry tomato", products).Product);
        var withCherry = products.Append(new(6, "Cherrytomaat")).ToArray();
        Assert.Equal("Cherrytomaat", CertificateProposals.MatchProduct("Cherry tomatoes", withCherry).Product?.Name);
    }

    [Fact]
    public void A_generic_company_word_is_not_enough_to_be_the_same_holder()
    {
        Assert.False(CertificateProposals.SameCompany("Kwekerij Jansen", "Kwekerij De Vries"));
        Assert.True(CertificateProposals.SameCompany("Kwekerij Jansen B.V.", "Jansen Kwekerij"));
    }

    [Fact]
    public void A_doubt_about_a_product_follows_it_after_empty_and_duplicate_products_are_dropped()
    {
        var json = new JsonObject
        {
            ["soort"] = "GLOBALG.A.P. IFA (GGN)", ["nummer"] = "4063061591012",
            ["producten"] = new JsonArray(
                new JsonObject { ["tekst"] = "" }, new JsonObject { ["tekst"] = "Lettuce" }, new JsonObject { ["tekst"] = "lettuce" }, new JsonObject { ["tekst"] = "Mizuna" }),
            ["zekerheden"] = new JsonArray(new JsonObject { ["veld"] = "producten[3]", ["zekerheid"] = "laag" }, new JsonObject { ["veld"] = "producten[0]", ["zekerheid"] = "laag" }),
        };
        CertificateProposals.Build(json, Array.Empty<CertificateProposals.OwnProduct>(), Array.Empty<CertificateProposals.StoredCertificate>(), Array.Empty<string>(),
            new DateOnly(2026, 1, 1), 30, out var problems);
        Assert.Contains(problems, x => (x.Field, x.Code) == ("products[1]", AiReadingRules.Uncertain));
        Assert.DoesNotContain(problems, x => (x.Field, x.Code) == ("products[0]", AiReadingRules.Uncertain));
    }

    [Fact]
    public void Two_own_products_with_the_same_translation_are_not_guessed()
    {
        var (product, kind) = CertificateProposals.MatchProduct("Lettuce", new[] { new CertificateProposals.OwnProduct(1, "Sla"), new(2, "sla") });
        Assert.Equal(((CertificateProposals.OwnProduct)null, CertificateProposals.MatchNone), (product, kind));
    }
}
