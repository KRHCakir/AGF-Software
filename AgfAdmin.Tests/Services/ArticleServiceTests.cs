using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Artikelwerkplek op het bestaande model: één keer vastleggen, daarna via zoekcode kiezen. Alleen de productsoort is
// verplicht; leeg blijft leeg; goederencodes zijn tekst met stelsel; wijzigingen raken nooit bestaande documenten.
public class ArticleServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<ArticleDto>> CreateAsync(SaveArticleDto request)
    {
        await using var db = _factory.CreateContext();
        return await new ArticleService(db).CreateAsync(request);
    }

    private async Task<ServiceResult<ArticleDto>> UpdateAsync(int id, SaveArticleDto request)
    {
        await using var db = _factory.CreateContext();
        return await new ArticleService(db).UpdateAsync(id, request);
    }

    private async Task<IReadOnlyList<ArticleDto>> ListAsync()
    {
        await using var db = _factory.CreateContext();
        return await new ArticleService(db).ListAsync();
    }

    private static SaveArticleDto Hass() => new()
    {
        ProductName = "Avocado", VariantName = "Hass", ShortcutCode = "avohassper22", Origin = "Peru", Packaging = "4kg Box", PackagingUnit = "box",
        WeightMethod = "Calculated", DefaultNetKgPerPackage = 4m, DefaultGrossKgPerPackage = 4.4m, DefaultPackagesPerPallet = 120m,
        GoodsCode = "0804 40 00", GoodsCodeSystem = "CN", TradeClass = "I", Brand = "Sol"
    };

    [Fact]
    public async Task Create_With_Only_A_Product_Kind_Leaves_Everything_Else_Empty_And_Weighed()
    {
        var created = await CreateAsync(new SaveArticleDto { ProductName = " Tomaat " });
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var article = created.Value;
        Assert.Equal("Tomaat", article.ProductName);
        Assert.Equal("", article.VariantName);
        Assert.Equal("Tomaat", article.Description);
        Assert.Equal(WeightRules.Weighed, article.WeightMethod);
        Assert.Null(article.ShortcutCode);
        Assert.Null(article.Origin);
        Assert.Null(article.PackagingUnit);
        Assert.Null(article.DefaultNetKgPerPackage);
        Assert.Null(article.DefaultGrossKgPerPackage);
        Assert.Null(article.DefaultPackagesPerPallet);
        Assert.Null(article.GoodsCode);
        Assert.Null(article.GoodsCodeSystem);
        Assert.Null(article.TradeClass);
        Assert.Equal(0m, article.DefaultPrice);
    }

    [Fact]
    public async Task Full_Article_Is_Stored_With_Defaults_And_Found_By_Search_Code_Case_Insensitively()
    {
        var created = (await CreateAsync(Hass())).Value;
        Assert.Equal("Avocado · Hass · 4kg Box · Peru", created.Description);
        Assert.Equal(("box", "Calculated", 4m, 4.4m, 120m), (created.PackagingUnit, created.WeightMethod, created.DefaultNetKgPerPackage, created.DefaultGrossKgPerPackage, created.DefaultPackagesPerPallet));
        Assert.Equal(("08044000", "CN", "I", "Sol"), (created.GoodsCode, created.GoodsCodeSystem, created.TradeClass, created.Brand));

        var list = await ListAsync();
        Assert.Single(list, a => string.Equals(a.ShortcutCode, "AVOHASSPER22", StringComparison.OrdinalIgnoreCase));

        // Zoekcode is uniek (hoofdletterongevoelig); een tweede artikel met dezelfde code wordt geweigerd met verwijzing.
        var duplicate = Hass();
        duplicate.VariantName = "Hass XL";
        duplicate.ShortcutCode = "AvoHassPer22";
        var rejected = await CreateAsync(duplicate);
        Assert.Equal(ServiceResultStatus.Validation, rejected.Status);
        Assert.Contains("shortcutCode", rejected.Errors.Keys);
        Assert.Contains($"#{created.Id}", rejected.Errors["shortcutCode"][0]);
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task Update_Can_Clear_Optional_Fields_And_Keeps_The_Search_Code_Of_The_Article_Itself()
    {
        var created = (await CreateAsync(Hass())).Value;
        var request = Hass();
        request.Brand = "  ";
        request.TradeClass = null;
        request.GoodsCode = "";
        request.GoodsCodeSystem = "CN"; // stelsel zonder code wordt niet vastgelegd
        request.DefaultGrossKgPerPackage = null;
        var updated = await UpdateAsync(created.Id, request);
        Assert.Equal(ServiceResultStatus.Ok, updated.Status);
        Assert.Equal("avohassper22", updated.Value.ShortcutCode);
        Assert.Null(updated.Value.Brand);
        Assert.Null(updated.Value.TradeClass);
        Assert.Null(updated.Value.GoodsCode);
        Assert.Null(updated.Value.GoodsCodeSystem);
        Assert.Null(updated.Value.DefaultGrossKgPerPackage);
        Assert.Equal(4m, updated.Value.DefaultNetKgPerPackage);
        Assert.Equal(ServiceResultStatus.NotFound, (await UpdateAsync(999, Hass())).Status);
    }

    [Theory]
    [InlineData("product-missing", "productName")]
    [InlineData("shortcut-with-space", "shortcutCode")]
    [InlineData("unit-unknown", "packagingUnit")]
    [InlineData("method-unknown", "weightMethod")]
    [InlineData("net-zero", "defaultNetKgPerPackage")]
    [InlineData("gross-below-net", "defaultGrossKgPerPackage")]
    [InlineData("cpp-four-decimals", "defaultPackagesPerPallet")]
    [InlineData("code-without-system", "goodsCodeSystem")]
    [InlineData("code-wrong-length", "goodsCode")]
    [InlineData("code-letters", "goodsCode")]
    [InlineData("system-unknown", "goodsCodeSystem")]
    [InlineData("trade-class-too-long", "tradeClass")]
    public async Task Invalid_Input_Is_Rejected_And_Nothing_Is_Stored(string invalidCase, string expectedErrorKey)
    {
        var request = Hass();
        switch (invalidCase)
        {
            case "product-missing": request.ProductName = " "; break;
            case "shortcut-with-space": request.ShortcutCode = "avo hass"; break;
            case "unit-unknown": request.PackagingUnit = "pallet"; break;
            case "method-unknown": request.WeightMethod = "Estimated"; break;
            case "net-zero": request.DefaultNetKgPerPackage = 0m; break;
            case "gross-below-net": request.DefaultGrossKgPerPackage = 3.9m; break;
            case "cpp-four-decimals": request.DefaultPackagesPerPallet = 120.0001m; break;
            case "code-without-system": request.GoodsCodeSystem = null; break;
            case "code-wrong-length": request.GoodsCodeSystem = "HS"; break; // 8 cijfers voor een HS-code van 6
            case "code-letters": request.GoodsCode = "0804AB"; request.GoodsCodeSystem = "HS"; break;
            case "system-unknown": request.GoodsCodeSystem = "GN"; break;
            case "trade-class-too-long": request.TradeClass = new string('x', 51); break;
        }
        var result = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Empty(await ListAsync());
    }

    [Theory]
    [InlineData("0804ABC4000", "CN")]   // letters worden nooit weggelaten (zou anders 08044000 worden)
    [InlineData("0804 40 0O", "CN")]    // hoofdletter O in plaats van nul
    [InlineData("0804-40-00", "CN")]    // koppelteken is geen toegestaan scheidingsteken
    [InlineData("0804/4000", "CN")]
    [InlineData("0804\t40\t00", "CN")]  // tab is geen toegestaan scheidingsteken
    [InlineData("080440", "HS")]        // controle: dezelfde opbouw met geldige cijfers wordt wel geaccepteerd (zie onder)
    public async Task Goods_Codes_With_Letters_Or_Other_Characters_Are_Rejected_Never_Silently_Stripped(string input, string system)
    {
        var result = await CreateAsync(new SaveArticleDto { ProductName = "Avocado", GoodsCode = input, GoodsCodeSystem = system });
        if (input == "080440")
        {
            Assert.Equal(ServiceResultStatus.Created, result.Status);
            return;
        }
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("goodsCode", result.Errors.Keys);
        Assert.Contains("digits only", result.Errors["goodsCode"][0]);
        Assert.Empty(await ListAsync());
    }

    [Theory]
    [InlineData("0702 00 00", "CN", "07020000")]
    [InlineData(" 0702.00.00 ", "CN", "07020000")]
    [InlineData("070200", "HS", "070200")]
    [InlineData("0702.00.00.10", "TARIC", "0702000010")]
    [InlineData("08044000", "CN", "08044000")]
    public async Task Goods_Codes_Are_Stored_As_Text_With_Their_Leading_Zeros(string input, string system, string stored)
    {
        var request = new SaveArticleDto { ProductName = "Tomaat", GoodsCode = input, GoodsCodeSystem = system.ToLowerInvariant() };
        var created = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(stored, created.Value.GoodsCode);
        Assert.Equal(system, created.Value.GoodsCodeSystem);
        await using var db = _factory.CreateContext();
        Assert.Equal(stored, (await db.ProductVariants.SingleAsync()).GoodsCode);
    }

    // GGN is geen artikelstandaard meer: het veld zit niet in de artikelinvoer, een oude kolomwaarde blijft bewaard en wordt via de
    // relatiewerkplek gerapporteerd voor handmatige toewijzing aan een leverancier (geen automatische verplaatsing: geen betrouwbare koppeling).
    [Fact]
    public async Task Article_Input_Has_No_Ggn_And_A_Legacy_Default_Stays_In_The_Column_Until_Assigned_By_Hand()
    {
        Assert.DoesNotContain(typeof(ArticleDto).GetProperties(), p => p.Name.Contains("Ggn") || p.Name.Contains("Certif"));
        Assert.DoesNotContain(typeof(SaveArticleDto).GetProperties(), p => p.Name.Contains("Ggn"));
        var created = (await CreateAsync(Hass())).Value;
        await using (var db = _factory.CreateContext())
        {
            (await db.ProductVariants.SingleAsync()).DefaultGgn = "0049929123456"; // waarde van vóór de verplaatsing
            await db.SaveChangesAsync();
        }
        var request = Hass();
        request.TradeClass = "II";
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(created.Id, request)).Status);
        await using var verify = _factory.CreateContext();
        Assert.Equal("0049929123456", (await verify.ProductVariants.SingleAsync()).DefaultGgn);
        var legacy = Assert.Single((await new RelationService(verify).ReferenceAsync()).LegacyArticleGgns);
        Assert.Equal((created.Id, "0049929123456"), (legacy.ProductVariantId, legacy.Ggn));
    }

    [Fact]
    public async Task Trade_Class_Is_Master_Data_And_Never_An_Inspection_Status()
    {
        var created = (await CreateAsync(Hass())).Value;
        Assert.Equal("I", created.TradeClass);
        // Het artikel kent geen keuringsveld: goedkeuren blijft een handeling op de inhoudsregel (PalletContentLine.QualityStatus).
        Assert.DoesNotContain(typeof(ArticleDto).GetProperties(), p => p.Name.Contains("Quality") || p.Name.Contains("Inspection") || p.Name.Contains("Approved"));
        Assert.DoesNotContain(typeof(SaveArticleDto).GetProperties(), p => p.Name.Contains("Quality") || p.Name.Contains("Inspection") || p.Name.Contains("Approved"));
        var request = Hass();
        request.TradeClass = "Industrie"; // vrije tekst blijft toegestaan (handelsbestemming), geen vaste "1-2-3-4"-indeling
        Assert.Equal("Industrie", (await UpdateAsync(created.Id, request)).Value.TradeClass);
    }

    [Fact]
    public async Task Article_Changes_Never_Touch_Existing_Documents_Or_Lines()
    {
        var created = (await CreateAsync(Hass())).Value;
        int invoiceLineId, orderLineId;
        await using (var db = _factory.CreateContext())
        {
            var vendor = new Vendor { Name = "The Greenery" };
            var invoice = new IncomingInvoice
            {
                Vendor = vendor, InvoiceNumber = "GR-1", Status = "Draft", TotalAmount = 484m, VATAmount = 84m,
                InvoiceLines = new List<IncomingInvoiceLine> { new() { ProductVariantId = created.Id, Description = "Avocado · Hass", Quantity = 100m, Unit = "box", UnitPrice = 4m, LineTotal = 400m, VATPercentage = 21m, VATAmount = 84m } }
            };
            var order = new SalesOrder
            {
                RequestId = Guid.NewGuid(), Customer = new Customer { Name = "Klant" }, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "Jan",
                Lines = new List<SalesOrderLine> { new() { ProductVariantId = created.Id, Quantity = 1080m, Unit = "box", PalletCount = 9m, PackagesPerPallet = 120m } }
            };
            db.AddRange(invoice, order);
            await db.SaveChangesAsync();
            invoiceLineId = invoice.InvoiceLines.Single().Id;
            orderLineId = order.Lines.Single().Id;
        }

        var request = Hass();
        request.VariantName = "Hass XL";
        request.PackagingUnit = "colli";
        request.DefaultPackagesPerPallet = 80m;
        request.DefaultNetKgPerPackage = 10m;
        request.DefaultGrossKgPerPackage = 11m;
        request.TradeClass = "II";
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(created.Id, request)).Status);

        await using var verify = _factory.CreateContext();
        var invoiceLine = await verify.IncomingInvoiceLines.SingleAsync(l => l.Id == invoiceLineId);
        Assert.Equal(("Avocado · Hass", "box", 100m), (invoiceLine.Description, invoiceLine.Unit, invoiceLine.Quantity));
        var orderLine = await verify.SalesOrderLines.SingleAsync(l => l.Id == orderLineId);
        Assert.Equal(("box", 1080m, 9m, 120m), (orderLine.Unit, orderLine.Quantity, orderLine.PalletCount, orderLine.PackagesPerPallet));
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.PalletContentLines.CountAsync());
    }

    [Fact]
    public async Task Changing_The_Product_Kind_Renames_A_Lonely_Kind_Or_Moves_To_An_Existing_One()
    {
        var hass = (await CreateAsync(Hass())).Value;
        var rename = Hass();
        rename.ProductName = "Avocado (Hass)";
        var renamed = (await UpdateAsync(hass.Id, rename)).Value;
        Assert.Equal("Avocado (Hass)", renamed.ProductName);
        Assert.Equal(hass.ProductId, renamed.ProductId); // het enige artikel van een soort hernoemt de soort zelf

        var kent = (await CreateAsync(new SaveArticleDto { ProductName = "Mango", VariantName = "Kent" })).Value;
        var move = Hass();
        move.ProductName = "mango"; // bestaande soort, andere schrijfwijze
        var moved = (await UpdateAsync(hass.Id, move)).Value;
        Assert.Equal(kent.ProductId, moved.ProductId);
        Assert.Equal("Mango", moved.ProductName);

        // De verlaten soort bestaat nog (geen stille verwijdering); een gedeelde soort wordt nooit hernoemd.
        await using var db = _factory.CreateContext();
        Assert.Equal(2, await db.Products.CountAsync());
        var split = new SaveArticleDto { ProductName = "Mango Kent los", VariantName = "Kent" };
        var splitResult = (await UpdateAsync(kent.Id, split)).Value;
        Assert.NotEqual(moved.ProductId, splitResult.ProductId);
        Assert.Equal("Mango", (await db.Products.SingleAsync(p => p.Id == moved.ProductId)).Name);
    }
}

public class GoodsCodeCatalogTests
{
    // GN-lijst met twee tomatencodes onder één HS-post (samenvoegen), een HS-post mét omschrijving (070200) en een zonder
    // (080440, 080450: omschrijving dan uit de onderliggende GN-codes). TARIC-regels alleen als de test ze expliciet opgeeft.
    private static GoodsCodeCatalog Catalog(bool withTaric = false)
    {
        var entries = new List<GoodsCodeMatchDto>
        {
            new() { Code = "0804 40 00", DescriptionNl = "Avocado's, vers of gedroogd", DescriptionEn = "Avocados, fresh or dried" },
            new() { Code = "08045000", DescriptionNl = "Guaves, manga's en manggistans", DescriptionEn = "Guavas, mangoes and mangosteens" },
            new() { Code = "07020010", DescriptionNl = "Tomaten, vers of gekoeld: cherrytomaten", DescriptionEn = "Tomatoes, fresh or chilled: cherry tomatoes" },
            new() { Code = "07020099", DescriptionNl = "Tomaten, vers of gekoeld: andere", DescriptionEn = "Tomatoes, fresh or chilled: other" },
        };
        if (withTaric)
        {
            entries.Add(new GoodsCodeMatchDto { Code = "0804 40 00 10", System = "TARIC", DescriptionNl = "Avocado's (TARIC-testreferentie)", DescriptionEn = "Avocados (TARIC test reference)" });
        }
        return new GoodsCodeCatalog(
            new GoodsCodeSourceDto { System = "CN", Name = "GN 2026", Version = "2026", ValidFrom = "2026-01-01" },
            entries,
            new List<GoodsCodeMatchDto> { new() { Code = "070200", DescriptionNl = "Tomaten, vers of gekoeld", DescriptionEn = "Tomatoes, fresh or chilled" } });
    }

    [Fact]
    public void Searches_By_Code_Prefix_Or_By_Words_In_Either_Language_And_Never_Claims_Completeness()
    {
        var catalog = Catalog();
        Assert.Equal(new[] { "08044000", "08045000" }, catalog.Search("0804").Select(m => m.Code));
        Assert.Equal(new[] { "08044000" }, catalog.Search("0804 40").Select(m => m.Code));
        Assert.Equal(new[] { "08045000" }, catalog.Search("mango").Select(m => m.Code));
        Assert.Equal(new[] { "07020010", "07020099" }, catalog.Search("Tomaten vers").Select(m => m.Code));
        Assert.Empty(catalog.Search("   "));
        var result = catalog.Result("avocado");
        Assert.False(result.Complete);
        Assert.Equal("CN", result.System);
        Assert.Equal(4, result.TotalEntries);
        Assert.Equal("CN", result.Matches.Single().System);
        Assert.Equal("080440", result.Matches.Single().HsCode);
        Assert.Null(result.Matches.Single().MergedCodes);
        Assert.Equal("GN 2026", result.Source.Name);
    }

    // Dezelfde zoekterm onder HS, GN en TARIC geeft uitsluitend codes van dat stelsel, met de juiste lengte.
    [Fact]
    public void The_Same_Term_Gives_Only_Codes_Of_The_Chosen_System_With_The_Right_Length()
    {
        var catalog = Catalog();
        var hs = catalog.Result("avocado", "HS");
        var cn = catalog.Result("avocado", "CN");
        var taric = catalog.Result("avocado", "TARIC");
        Assert.Equal(("HS", "080440", 6), (hs.System, hs.Matches.Single().Code, hs.Matches.Single().Code.Length));
        Assert.Equal("HS", hs.Matches.Single().System);
        Assert.Equal(new[] { "08044000" }, hs.Matches.Single().MergedCodes);
        Assert.Equal(("CN", "08044000", 8), (cn.System, cn.Matches.Single().Code, cn.Matches.Single().Code.Length));
        Assert.Equal(("TARIC", 0, 0), (taric.System, taric.Matches.Count, taric.TotalEntries));
        Assert.Equal(3, hs.TotalEntries);   // 070200, 080440, 080450
        Assert.Equal(4, cn.TotalEntries);
        Assert.All(catalog.EntriesFor("HS"), h => Assert.True(ArticleRules.IsGoodsCodeFor("HS", h.Code)));
        Assert.All(catalog.EntriesFor("CN"), c => Assert.True(ArticleRules.IsGoodsCodeFor("CN", c.Code)));
        Assert.Empty(catalog.Search("0804", "XX"));
        Assert.Empty(catalog.Search("0804", null));
    }

    [Fact]
    public void Hs_Merges_Duplicate_Sub_Codes_Into_One_Heading_With_A_Heading_Level_Description()
    {
        var catalog = Catalog();
        var tomatoes = Assert.Single(catalog.Search("tomaten", "HS"));
        Assert.Equal("070200", tomatoes.Code);
        Assert.Equal("Tomaten, vers of gekoeld", tomatoes.DescriptionNl);
        Assert.Equal("Tomatoes, fresh or chilled", tomatoes.DescriptionEn);
        Assert.Equal(new[] { "07020010", "07020099" }, tomatoes.MergedCodes);
        // Een term die alleen in een GN-omschrijving voorkomt vindt onder HS de post waar die code onder valt.
        Assert.Equal("070200", Assert.Single(catalog.Search("cherrytomaten", "HS")).Code);
        // Zonder HS-omschrijving in de lijst: de omschrijving van de onderliggende GN-code(s), nooit verzonnen.
        var mango = Assert.Single(catalog.Search("mango", "HS"));
        Assert.Equal(("080450", "Guaves, manga's en manggistans"), (mango.Code, mango.DescriptionNl));
        // Een langere (GN-)code onder HS vindt de bovenliggende post; een HS-prefix werkt ook.
        Assert.Equal("080440", Assert.Single(catalog.Search("08044000", "HS")).Code);
        Assert.Equal(new[] { "080440", "080450" }, catalog.Search("0804", "HS").Select(m => m.Code));
    }

    [Fact]
    public void Taric_Shows_Only_Explicit_Ten_Digit_References_And_Never_Appends_Zeros()
    {
        Assert.Empty(Catalog().Search("0804", "TARIC"));
        Assert.Empty(Catalog().Search("avocado", "TARIC"));
        var withTaric = Catalog(withTaric: true);
        var reference = Assert.Single(withTaric.Search("avocado", "TARIC"));
        Assert.Equal(("0804400010", "TARIC", "080440"), (reference.Code, reference.System, reference.HsCode));
        Assert.Equal(1, withTaric.Result("0804", "TARIC").TotalEntries);
        // De TARIC-regel lekt niet naar GN of HS als eigen code; onder HS telt zij mee als onderliggende code van 080440.
        Assert.DoesNotContain(withTaric.EntriesFor("CN"), e => e.Code.Length != 8);
        Assert.Equal(new[] { "08044000", "0804400010" }, Assert.Single(withTaric.Search("avocado", "HS")).MergedCodes);
        Assert.DoesNotContain(withTaric.EntriesFor("TARIC"), e => e.Code.EndsWith("0000", StringComparison.Ordinal) && e.Code == "0804400000");
    }

    [Fact]
    public void Missing_File_Gives_An_Empty_Catalog_Instead_Of_A_Startup_Failure()
    {
        var empty = GoodsCodeCatalog.Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.Empty(empty.Entries);
        Assert.Empty(empty.Search("0804"));
    }

    [Fact]
    public void The_Shipped_Reference_File_Loads_And_Carries_Its_Source()
    {
        var path = Path.Combine(AppContext.BaseDirectory, GoodsCodeCatalog.DefaultRelativePath);
        var catalog = GoodsCodeCatalog.Load(path);
        Assert.False(string.IsNullOrWhiteSpace(catalog.Source.Name));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Source.LookupUrl));
        Assert.All(catalog.Entries, e => Assert.True(ArticleRules.IsGoodsCodeFor(e.System, e.Code), $"{e.Code} is not a valid {e.System} code"));
        // Geen onderbouwde TARIC-referenties in de meegeleverde lijst: het antwoord onder TARIC is eerlijk leeg.
        Assert.Empty(catalog.EntriesFor("TARIC"));
        // Elke GN-code valt onder een HS-post met een omschrijving op postniveau; avocado: HS 080440, GN 08044000.
        var headings = catalog.EntriesFor("HS");
        Assert.All(catalog.Entries, e => Assert.Contains(headings, h => h.Code == e.HsCode && !string.IsNullOrWhiteSpace(h.DescriptionNl) && !string.IsNullOrWhiteSpace(h.DescriptionEn)));
        Assert.All(headings, h => Assert.Equal(6, h.Code.Length));
        Assert.Equal(new[] { "08044000" }, Assert.Single(catalog.Search("avocado", "HS")).MergedCodes);
        Assert.Equal("Avocado's, vers of gedroogd", Assert.Single(catalog.Search("avocado", "HS")).DescriptionNl);
        Assert.Equal(3, Assert.Single(catalog.Search("tomaten", "HS")).MergedCodes.Count);
    }
}
