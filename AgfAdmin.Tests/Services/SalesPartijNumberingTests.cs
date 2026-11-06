using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2: een tweede reeks voor verkoop-partijen met eigen formaat, eigen aantal cijfers en eigen teller; elke nieuwe verkooporder krijgt
// het volgende nummer, ook als er twee tegelijk worden opgeslagen. De dubbelcontrole werkt per reeks.
public sealed class SalesPartijNumberingTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public SalesPartijNumberingTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task SettingsAsync(string format = "5{JJ}{MM}{volgnr}", string salesFormat = null, int? digits = null, string mode = "Auto", string reset = "Yearly", int? salesDigits = null)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto
        {
            Mode = mode, Format = format, SequenceReset = reset, SalesFormat = salesFormat, SequenceDigits = digits, SalesSequenceDigits = salesDigits
        });
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
    }

    private async Task<ServiceResult<PartijSummaryDto>> PurchaseAsync(string number = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number });
    }

    private async Task<SalesOrderDto> SaleAsync() => (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;

    // Het nummer van de verkoop-partij van een order, rechtstreeks uit de database.
    private async Task<string> LotOfAsync(int orderId)
    {
        await using var db = _s.Factory.CreateContext();
        return await db.Partijen.Where(p => p.SalesOrderId == orderId).Select(p => p.Number).SingleAsync();
    }

    [Theory]
    [InlineData("5{JJ}{MM}")]
    [InlineData("5{JJ}{MM}{nr}")]
    [InlineData("5{JJ}{volgnr}{volgnr}")]
    [InlineData("5{JJ}{MM}{volgnr")]
    public async Task An_Invalid_Format_Is_Refused_For_Both_Series(string format)
    {
        Assert.NotNull(PartijRules.FormatError(format));
        await using var db = _s.Factory.CreateContext();
        var purchase = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = format, SequenceReset = "Yearly" });
        Assert.True(purchase.Errors.ContainsKey("format"));
        var sales = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = "{JJ}{MM}{volgnr}", SequenceReset = "Yearly", SalesFormat = format });
        Assert.True(sales.Errors.ContainsKey("salesFormat"));
        var digits = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = "{JJ}{MM}{volgnr}", SequenceReset = "Yearly", SequenceDigits = 0, SalesSequenceDigits = 10 });
        Assert.True(digits.Errors.ContainsKey("sequenceDigits"));
        Assert.True(digits.Errors.ContainsKey("salesSequenceDigits"));
    }

    [Fact]
    public async Task The_Two_Series_Count_Independently_With_Their_Own_Format()
    {
        await _s.ArrangeAsync();
        await SettingsAsync();
        Assert.Equal("52611001", (await PurchaseAsync()).Value.Number);
        Assert.Equal("52611002", (await PurchaseAsync()).Value.Number);
        var first = await SaleAsync();
        var second = await SaleAsync();
        Assert.Equal(("2611001", "2611002"), (await LotOfAsync(first.Id), await LotOfAsync(second.Id)));
        Assert.Equal("52611003", (await PurchaseAsync()).Value.Number);

        await using var db = _s.Factory.CreateContext();
        Assert.Equal("2611003", (await new PartijService(db).NextNumberAsync(kind: "Sales")).Value.Suggestion);
        Assert.Equal("52611004", (await new PartijService(db).NextNumberAsync()).Value.Suggestion);
        // Het aantal cijfers per reeks: 2 bij verkoop geeft 261103 (zoals 261140), inkoop blijft 3; een bestaand nummer blijft zoals het is.
        await SettingsAsync(salesDigits: 2);
        Assert.Equal("52611004", (await new PartijService(db).NextNumberAsync()).Value.Suggestion);
        Assert.Equal("261103", (await new PartijService(db).NextNumberAsync(kind: "Sales")).Value.Suggestion);
        await SettingsAsync(digits: 2);
        Assert.Equal("5261104", (await new PartijService(db).NextNumberAsync()).Value.Suggestion);
        Assert.Equal("52611001", (await db.Partijen.SingleAsync(p => p.Sequence == 1 && p.Kind == "Purchase")).Number);
    }

    [Fact]
    public async Task The_Duplicate_Check_Uses_The_Key_Within_The_Purchase_Series()
    {
        await _s.ArrangeAsync();
        await SettingsAsync();
        Assert.Equal(ServiceResultStatus.Created, (await PurchaseAsync("5261145")).Status);
        var duplicate = await PurchaseAsync("5261-145");
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);
        Assert.Equal("5261145", ((PartijExistsConflictDto)duplicate.ConflictBody).Partij.Number);
        await using var db = _s.Factory.CreateContext();
        Assert.NotNull(await new PartijService(db).FindByNumberAsync("5261 145"));
        Assert.Null(await new PartijService(db).FindByNumberAsync("5261145", "Sales"));
    }

    [Fact]
    public async Task Orders_Added_Together_Or_Simultaneously_Get_Different_Sales_Lots()
    {
        await _s.ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            db.SalesOrders.AddRange(
                new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _s.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "seed" },
                new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _s.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "seed" });
            await db.SaveChangesAsync();
        }

        // De eerste schrijver heeft 2611003 al gekozen; vóór zijn save neemt een gelijktijdige order dat nummer. De unieke index weigert
        // de eerste, die het opnieuw probeert met het volgende VO-nummer en de volgende verkoop-partij.
        await using var racing = _s.Factory.CreateContextBeforeGuard(async () => Assert.Equal("2611003", await LotOfAsync((await SaleAsync()).Id)));
        var request = _s.Order((5m, "Stock"));
        var result = await new SalesOrderService(racing).CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("2611004", await LotOfAsync(result.Value.Id));

        await using var verify = _s.Factory.CreateContext();
        Assert.Equal(new[] { "2611001", "2611002", "2611003", "2611004" }, await verify.Partijen.Where(p => p.Kind == "Sales").OrderBy(p => p.Number).Select(p => p.Number).ToListAsync());
        Assert.Equal(4, await verify.SalesOrders.CountAsync(o => o.SalesPartij != null));
    }

    [Fact]
    public async Task A_Sales_Lot_Shows_Where_Its_Colli_Come_From_And_The_Purchase_Lot_Names_It()
    {
        await _s.ArrangeAsync();
        var purchaseLot = (await PurchaseAsync("52611002")).Value.Id;
        await using (var link = _s.Factory.CreateContext())
        {
            var receivedOrder = await link.PalletContentLines.Where(c => c.Id == _s.StockContentId).Select(c => c.SupplyCommitmentLine.SupplyCommitmentId).SingleAsync();
            Assert.Equal(ServiceResultStatus.Ok, (await new PartijService(link).LinkOrderAsync(purchaseLot, new LinkPartijDto { SupplyCommitmentId = receivedOrder, Reason = "Test" })).Status);
        }
        var order = await SaleAsync();
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 5m, 30m)).Status);

        await using var db = _s.Factory.CreateContext();

        var service = new PartijService(db);
        Assert.Equal(0, (await service.ListAsync("2611001", "all", 1, 50)).Value.Total);
        var sales = (await service.ListAsync("2611001", "all", 1, 50, kind: "Sales")).Value;
        var item = Assert.Single(sales.Items);
        Assert.Equal(("Sales", order.OrderNumber, 5m, 5m), (item.Kind, item.SalesOrderNumber, item.OrderedQuantity, item.SoldQuantity));
        var detail = (await service.GetAsync(item.Id)).Value;
        var source = Assert.Single(detail.Sources);
        Assert.Equal((purchaseLot, "52611002", "Kweker K", 5m, 0m), (source.PartijId, source.Number, source.VendorName, source.ReservedQuantity, source.ShippedQuantity));
        Assert.Equal(new[] { "52611002" }, item.SourcePartijen);
        // De inkoop-partij noemt de verkoop-partij bij de verkooporder.
        Assert.Equal("2611001", (await service.GetAsync(purchaseLot)).Value.SalesOrders.Single().SalesPartijNumber);
    }
}
