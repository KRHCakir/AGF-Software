using System.Text.RegularExpressions;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgfAdmin.Tests.Services;

// Klasse is een vaste lijst (Extra, I, II): "I" en "1" zijn dezelfde klasse en bewaard wordt altijd de vaste waarde. Maat en klasse zijn
// verplicht bij een nieuw of gewijzigd artikel (verpakking en merk niet); bestaande artikelen zonder maat of klasse blijven bruikbaar en
// staan in "Maat en klasse opschonen". Guard-regel 21 vangt andere writers op.
public sealed class TradeClassTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("I", "I")]
    [InlineData("i", "I")]
    [InlineData("1", "I")]
    [InlineData(" 1 ", "I")]
    [InlineData("I.", "I")]
    [InlineData("klasse 1", "I")]
    [InlineData("Klasse I", "I")]
    [InlineData("KLASSE  i", "I")]
    [InlineData("class I", "I")]
    [InlineData("Class 1", "I")]
    [InlineData("cat. 1", "I")]
    [InlineData("Cat I", "I")]
    [InlineData("categorie 1", "I")]
    [InlineData("category I", "I")]
    [InlineData("kl. 1", "I")]
    [InlineData("klasse:1", "I")]
    [InlineData("handelsklasse I", "I")]
    [InlineData("II", "II")]
    [InlineData("ii", "II")]
    [InlineData("2", "II")]
    [InlineData("klasse 2", "II")]
    [InlineData("Klasse II", "II")]
    [InlineData("class II", "II")]
    [InlineData("cat. 2", "II")]
    [InlineData("I I", "II")]
    [InlineData("Extra", "Extra")]
    [InlineData("extra", "Extra")]
    [InlineData("EXTRA", "Extra")]
    [InlineData("0", "Extra")]
    [InlineData("e", "Extra")]
    [InlineData("E", "Extra")]
    [InlineData("klasse extra", "Extra")]
    [InlineData("Klasse Extra", "Extra")]
    [InlineData("class extra", "Extra")]
    [InlineData("cat. 0", "Extra")]
    public void Every_Spelling_Of_A_Class_Becomes_The_Fixed_Value(string typed, string expected) =>
        Assert.Equal(expected, ArticleRules.NormalizeTradeClass(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("klasse")]
    [InlineData("III")]
    [InlineData("3")]
    [InlineData("A")]
    [InlineData("klasse A")]
    [InlineData("Industrie")]
    [InlineData("1e")]
    [InlineData("I/II")]
    [InlineData("12")]
    [InlineData("ex")]
    [InlineData("extra fijn")]
    [InlineData("klassement")]
    public void Anything_Else_Is_Never_Accepted_Silently(string typed) => Assert.Null(ArticleRules.NormalizeTradeClass(typed));

    [Fact]
    public void I_And_1_Compare_As_The_Same_Class_Old_Unrecognised_Text_Only_As_Itself()
    {
        Assert.Equal(ArticleRules.TradeClassKey("I"), ArticleRules.TradeClassKey("1"));
        Assert.Equal(ArticleRules.TradeClassKey("Extra"), ArticleRules.TradeClassKey("klasse extra"));
        Assert.NotEqual(ArticleRules.TradeClassKey("I"), ArticleRules.TradeClassKey("II"));
        Assert.Equal(ArticleRules.TradeClassKey("klasse A"), ArticleRules.TradeClassKey("Klasse  a"));
        Assert.NotEqual(ArticleRules.TradeClassKey("klasse A"), ArticleRules.TradeClassKey("I"));
        Assert.Equal("", ArticleRules.TradeClassKey(null));
    }

    [Fact]
    public void Receipt_Deviation_I_Versus_1_Is_Not_A_Class_Difference()
    {
        ReceiptDeviationRules.VariantFacts Facts(int id, string tradeClass) => new(id, 1, "Avocado", "Hass", "Peru", "18", "4kg Box", "colli", tradeClass, null);
        Assert.Equal(new[] { ReceiptDeviationRules.KindOther }, ReceiptDeviationRules.DifferingKinds(Facts(1, "I"), Facts(2, "1")));
        Assert.Equal(new[] { ReceiptDeviationRules.KindTradeClass }, ReceiptDeviationRules.DifferingKinds(Facts(1, "I"), Facts(2, "II")));
        Assert.Equal(new[] { ReceiptDeviationRules.KindTradeClass }, ReceiptDeviationRules.DifferingKinds(Facts(1, "I"), Facts(2, null)));
    }

    // De migratie zet in SQL om met dezelfde regels als NormalizeTradeClass: hetzelfde voorvoegsel- en scheidingspatroon, en elke
    // sleutel in de CASE geeft dezelfde klasse als de C#-functie. Draait in Postgres (gecontroleerd op een kopie van agf_smoke).
    [Fact]
    public void Migration_Uses_The_Same_Rules_As_The_Normaliser()
    {
        var sql = new AgfAdmin.Migrations.NormalizeTradeClasses().UpOperations.OfType<SqlOperation>().Single().Sql;
        Assert.Contains($"'{ArticleRules.TradeClassPrefixPattern}'", sql);
        Assert.Contains($"'{ArticleRules.TradeClassSeparatorPattern}'", sql);
        var pairs = Regex.Matches(sql, @"WHEN '([^']*)' THEN '([^']*)'").Select(m => (Key: m.Groups[1].Value, Class: m.Groups[2].Value)).ToList();
        Assert.Equal(7, pairs.Count);
        foreach (var (key, tradeClass) in pairs)
        {
            Assert.Equal(tradeClass, ArticleRules.NormalizeTradeClass(key));
        }
        Assert.Equal(ArticleRules.TradeClasses.OrderBy(c => c), pairs.Select(p => p.Class).Distinct().OrderBy(c => c));
        Assert.Contains("m.class IS NOT NULL", sql); // niet herkend: ongewijzigd laten
        Assert.Empty(new AgfAdmin.Migrations.NormalizeTradeClasses().DownOperations);
    }

    private static SaveArticleDto Article(string size, string tradeClass, string packaging, string brand) => new()
    {
        ProductName = "Avocado", VariantName = "Hass", OriginCountryCode = "PE", SizeCategory = size, TradeClass = tradeClass, Packaging = packaging, Brand = brand,
    };

    public static IEnumerable<object[]> Combinations() =>
        from size in new[] { "18", null }
        from tradeClass in new[] { "1", null }
        from packaging in new[] { "4 kg doos", null }
        from brand in new[] { "Verita", null }
        select new object[] { size, tradeClass, packaging, brand };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Size_And_Class_Are_Required_Packaging_And_Brand_Are_Not(string size, string tradeClass, string packaging, string brand)
    {
        await using var db = _factory.CreateContext();
        var result = await new ArticleService(db).CreateAsync(Article(size, tradeClass, packaging, brand));
        if (size != null && tradeClass != null)
        {
            Assert.Equal(ServiceResultStatus.Created, result.Status);
            Assert.Equal(("18", "I", packaging, brand), (result.Value.SizeCategory, result.Value.TradeClass, result.Value.Packaging, result.Value.Brand));
            return;
        }
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Equal(size == null, result.Errors.ContainsKey("sizeCategory"));
        Assert.Equal(tradeClass == null, result.Errors.ContainsKey("tradeClass"));
        Assert.False(result.Errors.ContainsKey("packaging"));
        Assert.False(result.Errors.ContainsKey("brand"));
        Assert.Equal(0, await db.ProductVariants.CountAsync());
    }

    [Fact]
    public async Task A_Class_Longer_Than_The_Column_Is_Refused_Before_It_Is_Normalised()
    {
        await using var db = _factory.CreateContext();
        var result = await new ArticleService(db).CreateAsync(Article("18", "I" + new string(' ', 60), null, null));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("at most 50", result.Errors["tradeClass"].Single());
    }

    [Fact]
    public async Task A_New_Article_Without_Size_Or_Class_Gets_A_400_With_A_Clear_Message_And_N_V_T_Is_A_Real_Size()
    {
        await using var controllerDb = _factory.CreateContext();
        var controller = new AgfAdmin.Controllers.ArticlesController(new ArticleService(controllerDb), null, null, null);
        var refused = await controller.Create(Article(" ", "klasse A", null, null));
        var bad = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(refused);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(bad.Value);
        Assert.Contains("n.v.t.", problem.Errors["sizeCategory"].Single());
        Assert.Contains("Extra, I or II", problem.Errors["tradeClass"].Single());

        await using var db = _factory.CreateContext();
        var created = await new ArticleService(db).CreateAsync(Article("n.v.t.", "Extra", null, null));
        Assert.Equal(("n.v.t.", "Extra"), (created.Value.SizeCategory, created.Value.TradeClass));
    }

    [Fact]
    public async Task An_Existing_Article_Without_Size_Or_Class_Stays_Usable_Is_Listed_For_Cleanup_And_Must_Be_Completed_When_Edited()
    {
        var seed = await SalesTestData.SeedAsync(_factory); // oude artikelen: zonder maat en klasse
        int oldClass;
        await using (var db = _factory.CreateContext())
        {
            var odd = new ProductVariant { Product = new Product { Name = "Peer" }, Name = "Conference", Origin = "Nederland", OriginCountryCode = "NL", SizeCategory = "65+", TradeClass = "I", DefaultPrice = 0m };
            var clean = new ProductVariant { Product = new Product { Name = "Appel" }, Name = "Elstar", Origin = "Nederland", OriginCountryCode = "NL", SizeCategory = "70-75", TradeClass = "I", DefaultPrice = 0m };
            var archived = new ProductVariant { Product = new Product { Name = "Kiwi" }, Name = "Hayward", Origin = "Italië", OriginCountryCode = "IT", DefaultPrice = 0m, IsArchived = true, ArchivedAt = DateTime.UtcNow, ArchivedBy = "Jan" };
            db.AddRange(odd, clean, archived);
            await db.SaveChangesAsync();
            oldClass = odd.Id;
            // Een oude, niet herkende klasse zoals die vóór de vaste lijst kon bestaan (ruwe SQL gaat buiten de guard om).
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"ProductVariants\" SET \"TradeClass\" = 'klasse A' WHERE \"Id\" = {oldClass}");
        }

        // Bruikbaar: een aanvoer op het oude artikel wordt gewoon vastgelegd.
        await using (var db = _factory.CreateContext())
        {
            var supply = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
            {
                RequestId = Guid.NewGuid(), VendorId = (await db.Vendors.FirstAsync()).Id, Route = "Warehouse", Origin = "Peru", ExpectedAt = PurchaseOrderTestData.ExpectedAt,
                Lines = new List<CreateSupplyCommitmentLineDto> { new() { ProductVariantId = seed.MangoId, ExpectedUnit = "box", ExpectedQuantity = 5m } }
            });
            Assert.Equal(ServiceResultStatus.Created, supply.Status);
        }

        IReadOnlyList<SizeClassCleanupItemDto> cleanup;
        await using (var db = _factory.CreateContext())
        {
            cleanup = await new ArticleService(db).SizeClassCleanupAsync();
        }
        Assert.Equal(new[] { "Avocado · Hass · 4kg Box · Peru", "Mango · Kent · 4kg Box · Peru", "Peer · Conference · Nederland · 65+" }, cleanup.Select(i => i.Description));
        Assert.Equal(("65+", "klasse A"), (cleanup[2].SizeCategory, cleanup[2].TradeClass));

        // Bewerken zonder maat en klasse: geweigerd; met beide: opgeslagen en uit het overzicht.
        await using (var db = _factory.CreateContext())
        {
            var service = new ArticleService(db);
            var article = (await service.GetAsync(seed.MangoId)).Value;
            var edit = new SaveArticleDto { ProductName = article.ProductName, VariantName = article.VariantName, OriginCountryCode = "PE", Packaging = article.Packaging };
            var refused = await service.UpdateAsync(seed.MangoId, edit);
            Assert.Equal(ServiceResultStatus.Validation, refused.Status);
            Assert.True(refused.Errors.ContainsKey("sizeCategory") && refused.Errors.ContainsKey("tradeClass"));
            var noClass = await service.SetSizeClassAsync(oldClass, new SetSizeClassDto { SizeCategory = "65+", TradeClass = "A" });
            Assert.Equal(ServiceResultStatus.Validation, noClass.Status);
            var fixedUp = await service.SetSizeClassAsync(oldClass, new SetSizeClassDto { SizeCategory = " 65+ ", TradeClass = "kl. 2" });
            Assert.Equal(("65+", "II"), (fixedUp.Value.SizeCategory, fixedUp.Value.TradeClass));
            Assert.Equal(ServiceResultStatus.NotFound, (await service.SetSizeClassAsync(999, new SetSizeClassDto { SizeCategory = "1", TradeClass = "I" })).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            Assert.DoesNotContain(await new ArticleService(db).SizeClassCleanupAsync(), i => i.Id == oldClass);
        }
    }

    [Fact]
    public async Task Creating_From_Entry_Follows_The_Same_Rules_And_I_Equals_1()
    {
        int hass;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", OriginCountryCode = "PE", SizeCategory = "18", Packaging = "4 kg doos", TradeClass = "I", DefaultPrice = 0m };
            db.Add(variant);
            await db.SaveChangesAsync();
            hass = variant.Id;
            // De oude schrijfwijze "1", zoals die vóór de migratie kon bestaan (ruwe SQL gaat buiten de guard om).
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"ProductVariants\" SET \"TradeClass\" = '1' WHERE \"Id\" = {hass}");
        }

        CreateArticleFromEntryDto Entry(string size, string tradeClass) => new()
        {
            RequestId = Guid.NewGuid(), Source = "Receipt", TemplateVariantId = hass, OriginCountryCode = "PE", SizeCategory = size, Packaging = "4 kg doos", TradeClass = tradeClass, ActorReference = "Kees",
        };
        await using (var db = _factory.CreateContext())
        {
            var service = new ArticleFromEntryService(db);
            var missing = await service.CreateAsync(Entry("", ""));
            Assert.Equal(ServiceResultStatus.Validation, missing.Status);
            Assert.True(missing.Errors.ContainsKey("sizeCategory") && missing.Errors.ContainsKey("tradeClass"));
            Assert.Equal(ServiceResultStatus.Validation, (await service.CreateAsync(Entry("18", "klasse A"))).Status);

            // "I" bij een artikel met de oude schrijfwijze "1": hetzelfde artikel, nooit een tweede.
            var same = await service.CreateAsync(Entry("18", "I"));
            Assert.Equal(ServiceResultStatus.Conflict, same.Status);
        }
        await using (var db = _factory.CreateContext())
        {
            var created = await new ArticleFromEntryService(db).CreateAsync(Entry("16", "klasse 2"));
            Assert.Equal(("16", "II"), (created.Value.Article.SizeCategory, created.Value.Article.TradeClass));
        }
    }

    [Fact]
    public async Task Guard_Rule_21_A_New_Or_Changed_Class_Comes_From_The_List_An_Old_Value_Stays_Until_The_Class_Changes()
    {
        await using (var db = _factory.CreateContext())
        {
            db.Add(new ProductVariant { Product = new Product { Name = "Peer" }, Name = "Conference", TradeClass = "klasse A", DefaultPrice = 0m });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.Add(new ProductVariant { Product = new Product { Name = "Peer" }, Name = "Doyenné", TradeClass = null, DefaultPrice = 0m }); // oude writers: geen klasse
            await db.SaveChangesAsync();
        }

        // Een oude, niet herkende waarde zoals die vóór deze regel bestond (ruwe SQL gaat buiten de guard om).
        await using (var db = _factory.CreateContext())
        {
            var id = await db.ProductVariants.Select(v => v.Id).SingleAsync();
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"ProductVariants\" SET \"TradeClass\" = 'klasse A' WHERE \"Id\" = {id}");
        }
        await using (var db = _factory.CreateContext())
        {
            var old = await db.ProductVariants.SingleAsync();
            old.Notes = "blijft bruikbaar";
            await db.SaveChangesAsync(); // klasse niet gewijzigd
        }
        await using (var db = _factory.CreateContext())
        {
            var detached = await db.ProductVariants.AsNoTracking().SingleAsync();
            detached.Season = "winter";
            db.Update(detached); // losgekoppelde Update: alle kolommen gemarkeerd, klasse gelijk aan de opgeslagen waarde
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var detached = await db.ProductVariants.AsNoTracking().SingleAsync();
            detached.TradeClass = "klasse B";
            db.Update(detached);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var old = await db.ProductVariants.SingleAsync();
            old.TradeClass = "1";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var old = await db.ProductVariants.SingleAsync();
            old.TradeClass = "II";
            await db.SaveChangesAsync();
            Assert.Equal("II", (await db.ProductVariants.AsNoTracking().SingleAsync()).TradeClass);
        }
    }
}
