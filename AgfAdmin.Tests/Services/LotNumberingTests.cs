using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november, prompt 5: het partijnummer is de sleutel. Nieuwe partijen krijgen I- (inkoop) en V- (verkoop) voor jaar, maand en
// volgnummer; bestaande nummers blijven zoals ze zijn; zoeken zonder letter vindt beide soorten; een nummer is uniek over inkoop en verkoop
// samen (regel 41).
public sealed class LotNumberingTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public LotNumberingTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _s.Dispose();

    private async Task<ServiceResult<PartijSummaryDto>> PurchaseAsync(string number = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number });
    }

    private async Task<ServiceResult<SalesOrderDto>> SaleAsync(string partijNumber = null)
    {
        var request = _s.Order((5m, "Stock"));
        request.SalesPartijNumber = partijNumber;
        await using var db = _s.Factory.CreateContext();
        return await new SalesOrderService(db).CreateAsync(request);
    }

    [Fact]
    public async Task New_Lots_Get_I_For_Purchase_And_V_For_Sales_With_Year_Month_And_Sequence()
    {
        await _s.ArrangeAsync();
        Assert.Equal("I-2611001", (await PurchaseAsync()).Value.Number);
        Assert.Equal("I-2611002", (await PurchaseAsync()).Value.Number);
        Assert.Equal("V-2611001", (await SaleAsync()).Value.SalesPartijNumber);
        await using var db = _s.Factory.CreateContext();
        var settings = await new PartijService(db).GetSettingsAsync();
        Assert.Equal(("I-{JJ}{MM}{volgnr}", "V-{JJ}{MM}{volgnr}"), (settings.Format, settings.SalesFormat));
        Assert.Equal("I-261103", (await new PartijService(db).NextNumberAsync(digits: 2)).Value.Suggestion);
    }

    // De migratie zet alleen de oude standaardformaten om (ook de 5 vooraan); een zelf gekozen formaat en alle bestaande nummers blijven.
    [Theory]
    [InlineData("{JJ}{MM}{volgnr}", "{JJ}{MM}{volgnr}", "I-{JJ}{MM}{volgnr}", "V-{JJ}{MM}{volgnr}")]
    [InlineData("5{JJ}{MM}{volgnr}", "{JJ}{MM}{volgnr}", "I-{JJ}{MM}{volgnr}", "V-{JJ}{MM}{volgnr}")]
    [InlineData("P-{JJJJ}/{volgnr}", "VK{volgnr}", "P-{JJJJ}/{volgnr}", "VK{volgnr}")]
    public async Task The_Migration_Converts_Only_The_Old_Default_Formats_And_Keeps_Every_Number(string purchase, string sales, string expectedPurchase, string expectedSales)
    {
        await _s.ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto
            {
                Mode = "Auto", Format = purchase, SequenceReset = "Yearly", SalesFormat = sales
            })).Status);
        }
        var old = (await PurchaseAsync()).Value.Number;
        var oldSale = (await SaleAsync()).Value.SalesPartijNumber;

        await using (var db = _s.Factory.CreateContext())
        {
            // Accolades verdubbeld: ExecuteSqlRaw leest {..} als parameter, de migratie (migrationBuilder.Sql) niet.
            await db.Database.ExecuteSqlRawAsync(AddLotNumberingIV.SettingsSql.Replace("{", "{{").Replace("}", "}}"));
        }

        await using var verify = _s.Factory.CreateContext();
        var settings = await new PartijService(verify).GetSettingsAsync();
        Assert.Equal((expectedPurchase, expectedSales), (settings.Format, settings.SalesFormat));
        Assert.Equal(new[] { oldSale, old }.OrderBy(n => n), await verify.Partijen.OrderBy(p => p.Number).Select(p => p.Number).ToListAsync());
        // Het volgnummer loopt door in de periode: na 2611001 komt I-2611002, niet opnieuw 001.
        if (expectedPurchase.StartsWith("I-"))
        {
            Assert.Equal("I-2611002", (await PurchaseAsync()).Value.Number);
        }
    }

    [Theory]
    [InlineData("2611001")]
    [InlineData("2611 001")]
    [InlineData("2611-001")]
    public async Task Searching_Without_Letter_Dashes_Or_Spaces_Finds_Both_Kinds(string query)
    {
        await _s.ArrangeAsync();
        await PurchaseAsync();
        await SaleAsync();
        await using var db = _s.Factory.CreateContext();
        var found = (await new PartijService(db).ListAsync(query, "all", 1, 50, kind: "all")).Value.Items.Select(i => i.Number).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "I-2611001", "V-2611001" }, found);
        var onlyPurchase = (await new PartijService(db).ListAsync("I-2611001", "all", 1, 50, kind: "all")).Value.Items.Select(i => i.Number).ToList();
        Assert.Equal(new[] { "I-2611001" }, onlyPurchase);
    }

    [Fact]
    public async Task A_Correction_To_A_Number_Of_The_Other_Kind_Is_Refused_With_An_Explanation()
    {
        await _s.ArrangeAsync();
        var purchase = (await PurchaseAsync("2610251")).Value;
        var sale = (await SaleAsync()).Value;
        await using var db = _s.Factory.CreateContext();
        var corrected = await new PartijService(db).CorrectNumberAsync(sale.SalesPartijId!.Value, new CorrectPartijNumberDto { Number = "2610-251", Reason = "Test" });
        Assert.Equal(ServiceResultStatus.Validation, corrected.Status);
        Assert.Contains("already exists as a purchase lot", corrected.Errors["number"].Single());
        var back = await new PartijService(db).CorrectNumberAsync(purchase.Id, new CorrectPartijNumberDto { Number = sale.SalesPartijNumber, Reason = "Test" });
        Assert.Equal(ServiceResultStatus.Validation, back.Status);
        Assert.Contains("already exists as a sales lot", back.Errors["number"].Single());
    }

    // Met de oude formaten (zonder letter) delen de reeksen dezelfde getallen: een voorstel slaat een nummer van de andere soort over.
    [Fact]
    public async Task A_Suggestion_Skips_A_Number_That_Exists_As_The_Other_Kind()
    {
        await _s.ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = "{JJ}{MM}{volgnr}", SequenceReset = "Yearly", SalesFormat = "{JJ}{MM}{volgnr}" });
        }
        Assert.Equal("2611001", (await PurchaseAsync()).Value.Number);
        Assert.Equal("2611002", (await SaleAsync()).Value.SalesPartijNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Guard_Refuses_A_Lot_Whose_Number_Exists_As_The_Other_Kind(bool async)
    {
        await _s.ArrangeAsync();
        var sale = (await SaleAsync()).Value;
        await using var db = _s.Factory.CreateContext();
        db.Partijen.Add(new Partij { Kind = PartijRules.KindPurchase, Number = "V 2611-001", NumberKey = PartijRules.NumberKey("V 2611-001"), CreatedBy = "test" });
        var error = async ? await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync()) : Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());
        Assert.Contains("never share a number", error.Message);

        // Ook een gewijzigd nummer (buiten de service om) en twee nieuwe partijen van verschillende soort in één save.
        db.ChangeTracker.Clear();
        var purchase = (await PurchaseAsync("2610251")).Value;
        var stored = await db.Partijen.SingleAsync(p => p.Id == purchase.Id);
        stored.Number = sale.SalesPartijNumber;
        stored.NumberKey = PartijRules.NumberKey(sale.SalesPartijNumber);
        Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.Partijen.Add(new Partij { Kind = PartijRules.KindPurchase, Number = "X1", NumberKey = "X1", CreatedBy = "test" });
        db.Partijen.Add(new Partij { Kind = PartijRules.KindSales, Number = "X-1", NumberKey = "X1", CreatedBy = "test", SalesOrderId = sale.Id });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
