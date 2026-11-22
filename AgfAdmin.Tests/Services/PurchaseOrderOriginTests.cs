using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: de herkomst van een inkooporder is een landcode (ISO 3166-1 alfa-2), geen vrije tekst. De code is de bron, de
// tekst de Nederlandse naam; een oude tekst die niet eenduidig een land was blijft staan zonder code ("Herkomst controleren").
public class PurchaseOrderOriginTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public PurchaseOrderOriginTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("PE", null, "PE")]
    [InlineData(" pe ", null, "PE")]
    [InlineData(null, "Peru", "PE")]
    [InlineData(null, "peru", "PE")]
    [InlineData(null, "Zuid-Afrika", "ZA")]
    [InlineData(null, "South Africa", "ZA")]
    [InlineData(null, "Turkije", "TR")]
    [InlineData(null, "Türkiye", "TR")]
    [InlineData("CR", "Peru", "CR")]   // de code wint
    public void A_Code_Or_An_Unambiguous_Name_Becomes_The_Country(string code, string text, string expected)
    {
        var errors = new List<string>();
        Assert.Equal(expected, SupplyRules.OriginCountry(code, text, (key, _) => errors.Add(key))?.Code);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null, "Peru (DEMO)", "origin")]
    [InlineData(null, "EU", "origin")]
    [InlineData("XX", null, "originCountryCode")]
    public void Anything_Else_Is_Refused_With_A_Clear_Field(string code, string text, string field)
    {
        var errors = new List<string>();
        Assert.Null(SupplyRules.OriginCountry(code, text, (key, _) => errors.Add(key)));
        Assert.Equal(new[] { field }, errors);
    }

    [Fact]
    public void Empty_Means_No_Origin()
    {
        Assert.Null(SupplyRules.OriginCountry(" ", "", (_, _) => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task A_New_Order_Stores_The_Code_And_The_Dutch_Name_And_No_Legacy_Text()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Origin = null;
        request.OriginCountryCode = "ZA";
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        Assert.Equal(("Zuid-Afrika", "ZA"), (created.Origin, created.OriginCountryCode));
        await using var db = _factory.CreateContext();
        var stored = await db.SupplyCommitments.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal(("ZA", "Zuid-Afrika", (string)null), (stored.OriginCountryCode, stored.Origin, stored.OriginLegacyText));
        var detail = await _h.DetailAsync(created.Id);
        Assert.Equal("ZA", detail.OriginCountryCode);
    }

    // Aanpassen (Open): een ander land wordt vastgelegd in de auditregel met de oude en nieuwe code; leeg maken kan ook.
    [Fact]
    public async Task Editing_The_Country_Is_Audited_And_Clearing_Works()
    {
        var order = await _h.OrderAsync();
        Assert.Equal("PE", order.OriginCountryCode);
        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.OriginCountryCode = "CR";
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal(("CR", "Costa Rica"), (saved.Value.OriginCountryCode, saved.Value.Origin));
        await using (var db = _factory.CreateContext())
        {
            var change = await db.PurchaseOrderChanges.AsNoTracking().SingleAsync(c => c.SupplyCommitmentId == order.Id);
            Assert.Contains(PurchaseOrderDocumentRules.Deserialize(change.ChangesJson), f => f.Field == "origin" && f.Old == "PE" && f.New == "CR");
        }
        var clear = PurchaseOrderEditHarness.EditOf(saved.Value);
        clear.OriginCountryCode = null;
        var cleared = await _h.RunAsync(s => s.UpdateAsync(order.Id, clear));
        Assert.Equal(((string)null, ""), (cleared.Value.OriginCountryCode, cleared.Value.Origin));
    }

    // Een oude order met alleen een tekst ("Peru (DEMO)"): aanpassen zonder land laat de tekst staan; een land kiezen zet het land.
    [Fact]
    public async Task An_Old_Text_Stays_Until_A_Country_Is_Chosen_And_Then_Shows_Up_No_More_In_The_Check()
    {
        var order = await _h.OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            // Zoals na de migratie: tekst zonder land, de oude tekst bewaard (ruwe SQL, zoals de migratie, buiten de guard om).
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"SupplyCommitments\" SET \"Origin\" = 'Peru (DEMO)', \"OriginCountryCode\" = NULL, \"OriginLegacyText\" = 'Peru (DEMO)' WHERE \"Id\" = {order.Id}");
        }
        await using (var db = _factory.CreateContext())
        {
            var check = Assert.Single(await new PurchaseOrderService(db).OriginCheckAsync());
            Assert.Equal((order.Id, "Peru (DEMO)", false), (check.Id, check.Origin, check.Definitive));
            Assert.Equal(new[] { "PE" }, check.Suggestions);
        }
        var legacy = await _h.DetailAsync(order.Id);
        var edit = PurchaseOrderEditHarness.EditOf(legacy);
        edit.Note = "alleen de notitie";
        var kept = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, kept.Status);
        Assert.Equal(("Peru (DEMO)", (string)null), (kept.Value.Origin, kept.Value.OriginCountryCode));

        var choose = PurchaseOrderEditHarness.EditOf(kept.Value);
        choose.OriginCountryCode = "PE";
        var chosen = await _h.RunAsync(s => s.UpdateAsync(order.Id, choose));
        Assert.Equal(("Peru", "PE"), (chosen.Value.Origin, chosen.Value.OriginCountryCode));
        await using (var db = _factory.CreateContext())
        {
            Assert.Empty(await new PurchaseOrderService(db).OriginCheckAsync());
            Assert.Equal("Peru (DEMO)", (await db.SupplyCommitments.AsNoTracking().SingleAsync(c => c.Id == order.Id)).OriginLegacyText); // audit blijft
        }
    }

    // Regel 29 blijft: een definitieve order verandert haar herkomst (land of tekst) niet buiten een auditregel om.
    [Fact]
    public async Task Guard_Rule_29_The_Country_Of_A_Definitive_Order_Does_Not_Change()
    {
        var order = await _h.OrderAsync();
        var finalized = await _h.RunAsync(s => s.FinalizeAsync(order.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = order.EditVersion }));
        Assert.Equal(ServiceResultStatus.Ok, finalized.Status);
        await using var db = _factory.CreateContext();
        var stored = await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id);
        stored.OriginCountryCode = "CR";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    // Regel 35: de oude tekst zet alleen de migratie; de applicatie zet of wijzigt hem nooit.
    [Fact]
    public async Task Guard_Rule_35_The_Legacy_Text_Is_Never_Set_Or_Changed_By_The_Application()
    {
        var order = await _h.OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            var stored = await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id);
            stored.OriginLegacyText = "Peru";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"SupplyCommitments\" SET \"OriginLegacyText\" = 'peru' WHERE \"Id\" = {order.Id}");
            var stored = await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id);
            stored.OriginLegacyText = null;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        var seed = _h.Seed;
        await using (var db = _factory.CreateContext())
        {
            var copy = await db.SupplyCommitments.AsNoTracking().SingleAsync(c => c.Id == order.Id);
            db.SupplyCommitments.Add(new AgfAdmin.Models.SupplyCommitment
            {
                RequestId = Guid.NewGuid(), VendorId = seed.VendorId, Route = "Warehouse", Status = "Expected", Origin = "", OriginLegacyText = "Peru",
                ExpectedAt = copy.ExpectedAt, OrderNumber = "IO-TEST-9999", OrderYear = 2099, OrderSequence = 9999, OrderedOn = copy.OrderedOn, Currency = "EUR", ExchangeRate = 1m,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    // De migratie: eerst de oude tekst bewaren, dan alleen eenduidige teksten omzetten (dezelfde sleutel als bij de artikelen).
    [Fact]
    public void The_Migration_Keeps_The_Old_Text_And_Maps_Only_Unambiguous_Texts()
    {
        var sql = OriginCountryOnPurchaseOrder.MapSql;
        Assert.True(sql.IndexOf("SET \"\"OriginLegacyText\"\" = \"\"Origin\"\"".Replace("\"\"", "\""), StringComparison.Ordinal) < sql.IndexOf("SET \"OriginCountryCode\"", StringComparison.Ordinal));
        Assert.Contains("m.countries = 1", sql);
        Assert.Contains("lower(c.\"NameNl\")", sql);
        Assert.Contains("lower(c.\"NameEn\")", sql);
    }
}
