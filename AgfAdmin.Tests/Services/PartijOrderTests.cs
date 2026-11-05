using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Inkooporder met partij: een nieuwe partij in dezelfde save (voorstel of eigen nummer), een tweede order op dezelfde partij, gelijktijdig
// aanmaken, de dubbelmelding en later koppelen (vóór ontvangst vrij, na ontvangst alleen zonder partij en met reden).
public class PartijOrderTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 27)) };
    private PurchaseOrderSeed _seed;

    public void Dispose() => _factory.Dispose();

    private async Task<CreateSupplyCommitmentDto> RequestAsync(int? partijId = null, string newNumber = null, bool newPartij = false, int? vendorId = null)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(_seed, vendorId: vendorId);
        request.PartijId = partijId;
        request.NewPartij = newPartij || newNumber != null ? new NewPartijForOrderDto { Number = newNumber } : null;
        request.SupplierLotReference = "NP-LOT-88";
        return request;
    }

    private async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request)
    {
        await using var db = _factory.CreateContext();
        return await new SupplyCommitmentService(db).CreateAsync(request);
    }

    private async Task<ServiceResult<PalletContentLineDto>> ReceiveAsync(int lineId, string lot = null)
    {
        await using var db = _factory.CreateContext();
        var pallet = await new PalletService(db).CreatePalletForTestAsync();
        return await new PalletService(db).AddContentLineAsync(pallet, new AddPalletContentLineDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = lineId, LotNumber = lot, Quantity = 10m, Unit = "box", NetWeightKg = 40m
        });
    }

    [Fact]
    public async Task New_Order_With_The_Suggested_Lot_Creates_It_In_The_Same_Save_And_The_Lines_Inherit_It()
    {
        var request = await RequestAsync(newPartij: true);
        var created = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal("2610001", created.Value.PartijNumber);
        Assert.Equal("NP-LOT-88", created.Value.SupplierLotReference);
        Assert.All(created.Value.Lines, l => { Assert.Equal(created.Value.PartijId, l.PartijId); Assert.Equal("2610001", l.PartijNumber); });

        // Een herhaling van dezelfde aanvraag is dezelfde order met dezelfde partij, geen tweede partij.
        var retry = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(created.Value.Id, retry.Value.Id);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Partijen.CountAsync());
    }

    [Fact]
    public async Task Two_Orders_From_Two_Suppliers_On_One_Lot_And_Receipts_Of_Both_Belong_To_It()
    {
        var first = (await CreateAsync(await RequestAsync(newNumber: "2610251"))).Value;
        var second = await CreateAsync(await RequestAsync(partijId: first.PartijId, vendorId: _seed.OtherVendorId));
        Assert.Equal(ServiceResultStatus.Created, second.Status);
        Assert.Equal("2610251", second.Value.PartijNumber);

        var a = await ReceiveAsync(first.Lines[0].Id);
        var b = await ReceiveAsync(second.Value.Lines[0].Id);
        Assert.Equal((first.PartijId, "2610251"), (a.Value.PartijId, a.Value.LotNumber));
        Assert.Equal((first.PartijId, "2610251"), (b.Value.PartijId, b.Value.LotNumber));
    }

    [Fact]
    public async Task Almost_Equal_Lots_Are_Never_Mixed()
    {
        var a = (await CreateAsync(await RequestAsync(newNumber: "2610251"))).Value;
        var b = (await CreateAsync(await RequestAsync(newNumber: "2610252"))).Value;
        Assert.NotEqual(a.PartijId, b.PartijId);

        Assert.Equal(ServiceResultStatus.Conflict, (await ReceiveAsync(a.Lines[0].Id, "2610252")).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ReceiveAsync(b.Lines[0].Id, "2610251")).Status);
        Assert.Equal(b.PartijId, (await ReceiveAsync(b.Lines[0].Id, "2610-252")).Value.PartijId);
    }

    [Fact]
    public async Task Existing_Own_Number_Asks_To_Use_The_Existing_Lot_And_Saves_Nothing()
    {
        var first = (await CreateAsync(await RequestAsync(newNumber: "2610251"))).Value;
        var again = await CreateAsync(await RequestAsync(newNumber: "2610 251"));

        Assert.Equal(ServiceResultStatus.Conflict, again.Status);
        Assert.Equal(first.PartijId, Assert.IsType<PartijExistsConflictDto>(again.ConflictBody).Partij.Id);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.SupplyCommitments.CountAsync());
    }

    [Fact]
    public async Task Existing_And_New_Lot_Together_Or_Own_Mode_Without_Number_Are_Refused()
    {
        var partij = await PartijTestData.CreateAsync(_factory, "2610251");
        var both = await CreateAsync(await RequestAsync(partijId: partij, newPartij: true));
        Assert.Equal(ServiceResultStatus.Validation, both.Status);

        await using (var db = _factory.CreateContext())
        {
            await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = "Own", Format = "{JJ}{MM}{volgnr}", SequenceReset = "Yearly" });
        }
        var own = await CreateAsync(await RequestAsync(newPartij: true));
        Assert.Equal(ServiceResultStatus.Validation, own.Status);
        Assert.Contains("newPartij.number", own.Errors.Keys);
        // Zonder partij blijft een order gewoon mogelijk (het scherm waarschuwt alleen).
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(await RequestAsync())).Status);
    }

    [Fact]
    public async Task Two_Simultaneous_Orders_With_A_Suggested_Lot_Get_Two_Different_Numbers()
    {
        var concurrent = await RequestAsync(newPartij: true);
        var request = await RequestAsync(newPartij: true);
        await using var db = _factory.CreateContextBeforeGuard(async () => Assert.Equal("2610001", (await CreateAsync(concurrent)).Value.PartijNumber));
        var result = await new SupplyCommitmentService(db).CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("2610002", result.Value.PartijNumber);
    }

    [Fact]
    public async Task Linking_Before_Receipt_Is_Free_After_Receipt_The_Lot_Is_Fixed()
    {
        var order = (await CreateAsync(await RequestAsync(newNumber: "2610251"))).Value;
        var other = await PartijTestData.CreateAsync(_factory, "2610261");

        await using (var db = _factory.CreateContext())
        {
            var relinked = await new PartijService(db).LinkOrderAsync(other, new LinkPartijDto { SupplyCommitmentId = order.Id });
            Assert.Equal(ServiceResultStatus.Ok, relinked.Status);
        }
        await ReceiveAsync(order.Lines[0].Id);
        await using var verify = _factory.CreateContext();
        var back = await new PartijService(verify).LinkOrderAsync(order.PartijId.Value, new LinkPartijDto { SupplyCommitmentId = order.Id, Reason = "toch de eerste" });
        Assert.Equal(ServiceResultStatus.Conflict, back.Status);
        Assert.All(await verify.PalletContentLines.AsNoTracking().ToListAsync(), c => Assert.Equal(other, c.PartijId));
    }

    [Fact]
    public async Task Retry_Is_Still_The_Same_Order_After_The_Lot_Was_Linked_Or_Relinked_Later()
    {
        var plain = await RequestAsync();
        var order = (await CreateAsync(plain)).Value;
        var own = await RequestAsync(newNumber: "2610251");
        var ownOrder = (await CreateAsync(own)).Value;
        var other = await PartijTestData.CreateAsync(_factory, "2610261");

        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new PartijService(db).LinkOrderAsync(other, new LinkPartijDto { SupplyCommitmentId = order.Id })).Status);
            Assert.Equal(ServiceResultStatus.Ok, (await new PartijService(db).LinkOrderAsync(other, new LinkPartijDto { SupplyCommitmentId = ownOrder.Id })).Status);
        }

        var retry = await CreateAsync(plain);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(order.Id, retry.Value.Id);
        var ownRetry = await CreateAsync(own);
        Assert.Equal(ServiceResultStatus.Ok, ownRetry.Status);
        Assert.Equal(ownOrder.Id, ownRetry.Value.Id);

        // Een andere partij in de aanvraag blijft een conflict, ook als de order inmiddels aan die partij hangt.
        plain.PartijId = other;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(plain)).Status);
    }

    [Fact]
    public async Task Received_Order_Without_Lot_Is_Linked_With_A_Reason_And_Keeps_Its_Lot_Numbers()
    {
        var order = (await CreateAsync(await RequestAsync())).Value;
        await ReceiveAsync(order.Lines[0].Id, "OUD-7");
        var partij = await PartijTestData.CreateAsync(_factory, "2610251");

        await using var db = _factory.CreateContext();
        var service = new PartijService(db);
        Assert.Equal(ServiceResultStatus.Validation, (await service.LinkOrderAsync(partij, new LinkPartijDto { SupplyCommitmentId = order.Id })).Status);
        var linked = await service.LinkOrderAsync(partij, new LinkPartijDto { SupplyCommitmentId = order.Id, Reason = "Partij stond op de pakbon" });
        Assert.Equal(ServiceResultStatus.Ok, linked.Status);

        await using var verify = _factory.CreateContext();
        var content = await verify.PalletContentLines.AsNoTracking().SingleAsync();
        Assert.Equal((partij, "OUD-7"), (content.PartijId, content.LotNumber));
        var audit = await verify.PartijAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal(("OrderLinked", order.Id, "Partij stond op de pakbon"), (audit.EventType, audit.SupplyCommitmentId.Value, audit.Reason));
    }
}
