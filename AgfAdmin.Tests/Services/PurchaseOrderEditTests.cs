using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Stap 4a: een inkooporder blijft aanpasbaar tot hij definitief is. Aanpassen schrijft één auditregel met oud en nieuw per veld
// (actor uit CurrentUser); na ontvangst blijft een regel staan met haar artikel, nooit onder ontvangen, en ligt de leverancier vast.
public class PurchaseOrderEditTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public PurchaseOrderEditTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Editing_An_Open_Order_Saves_With_One_Audit_Record_And_Is_Idempotent()
    {
        var order = await _h.OrderAsync();
        Assert.Equal(("Open", 0, true, false), (order.DocumentStatus, order.EditVersion, order.Editable, order.Lines.Single().InUse));

        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.SupplierReference = "NP-9";
        edit.Note = "Bel bij aankomst";
        edit.Lines[0].UnitPrice = 12.5m;
        edit.Lines[0].ExpectedQuantity = 200m;
        edit.Lines.Add(new UpdatePurchaseOrderLineDto { ProductVariantId = _h.Seed.KentId, ExpectedQuantity = 10m, ExpectedUnit = "box", ExpectedNetWeightKg = 40m, WeightSource = "Calculated", UnitPrice = 9m });
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal(("NP-9", "Bel bij aankomst", 2, 1), (saved.Value.SupplierReference, saved.Value.Note, saved.Value.Lines.Count, saved.Value.EditVersion));
        var change = Assert.Single(saved.Value.Changes);
        Assert.Equal(("Edit", PurchaseOrderEditHarness.Actor), (change.Kind, change.ActorReference));
        Assert.Contains(change.Fields, f => f.Field == "supplierReference" && f.Old == "NP-88123" && f.New == "NP-9");
        Assert.Contains(change.Fields, f => f.Field == "unitPrice" && f.LineId == order.Lines[0].Id && f.Old == null && f.New == "12.5");
        Assert.Contains(change.Fields, f => f.Field == "expectedQuantity" && f.Old == "221" && f.New == "200");
        Assert.Contains(change.Fields, f => f.Field == "line" && f.Action == "added" && f.New!.StartsWith("Mango Kent: 10 box", StringComparison.Ordinal));
        Assert.Contains(saved.Value.History, h => h.Type == "Edited" && h.ActorReference == PurchaseOrderEditHarness.Actor);

        // Hetzelfde verzoek nog eens: dezelfde order, geen tweede auditregel. Een verouderde versie: 409. Niets gewijzigd: geen auditregel.
        Assert.Single((await _h.RunAsync(s => s.UpdateAsync(order.Id, edit))).Value.Changes);
        var stale = PurchaseOrderEditHarness.EditOf(order);
        stale.Note = "Te laat";
        Assert.Equal((ServiceResultStatus.Conflict, PurchaseOrderService.StaleCode), ((await _h.RunAsync(s => s.UpdateAsync(order.Id, stale))).Status, PurchaseOrderEditHarness.CodeOf(await _h.RunAsync(s => s.UpdateAsync(order.Id, stale)))));
        var unchanged = await _h.RunAsync(s => s.UpdateAsync(order.Id, PurchaseOrderEditHarness.EditOf(saved.Value)));
        Assert.Equal((1, 1), (unchanged.Value.EditVersion, unchanged.Value.Changes.Count));

        // Een regel verwijderen staat als verwijderd in de historie.
        var remove = PurchaseOrderEditHarness.EditOf(saved.Value);
        remove.Lines.RemoveAt(1);
        var removed = await _h.RunAsync(s => s.UpdateAsync(order.Id, remove));
        Assert.Single(removed.Value.Lines);
        Assert.Contains(removed.Value.Changes[^1].Fields, f => f.Field == "line" && f.Action == "removed" && f.Old!.StartsWith("Mango Kent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_A_Partial_Receipt_The_Limits_Are_Explained_And_Price_Stays_Editable()
    {
        var order = await _h.OrderAsync();
        await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 40m, 160m);
        order = await _h.DetailAsync(order.Id);
        Assert.Equal((true, true, true), (order.VendorFixed, order.PartijFixed, order.Lines.Single().InUse));

        async Task<IReadOnlyDictionary<string, string[]>> Refused(Action<UpdatePurchaseOrderDto> change)
        {
            var edit = PurchaseOrderEditHarness.EditOf(order);
            change(edit);
            var result = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
            Assert.Equal(ServiceResultStatus.Validation, result.Status);
            return result.Errors;
        }
        Assert.Contains("lines[0].expectedQuantity", (await Refused(e => e.Lines[0].ExpectedQuantity = 39m)).Keys);
        Assert.Contains("lines[0].productVariantId", (await Refused(e => e.Lines[0].ProductVariantId = _h.Seed.KentId)).Keys);
        Assert.Contains("lines", (await Refused(e => e.Lines[0] = new UpdatePurchaseOrderLineDto { ProductVariantId = _h.Seed.KentId, ExpectedQuantity = 5m, ExpectedUnit = "box", ExpectedNetWeightKg = 20m, WeightSource = "Calculated" })).Keys);
        Assert.Contains("vendorId", (await Refused(e => e.VendorId = _h.Seed.OtherVendorId)).Keys);
        Assert.Contains("partijId", (await Refused(e => e.NewPartij = new NewPartijForOrderDto { Number = "2611001" })).Keys);

        var ok = PurchaseOrderEditHarness.EditOf(order);
        ok.Lines[0].ExpectedQuantity = 40m;
        ok.Lines[0].UnitPrice = 11m;
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, ok));
        Assert.Equal((ServiceResultStatus.Ok, 40m, 11m), (saved.Status, saved.Value.Lines.Single().ExpectedQuantity, saved.Value.Lines.Single().UnitPrice));
        Assert.Equal("Received", saved.Value.Status);
    }

    [Fact]
    public async Task A_Lot_Is_Set_On_The_Order_And_Its_Lines_And_Never_Removed()
    {
        var order = await _h.OrderAsync();
        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.NewPartij = new NewPartijForOrderDto { Number = "2611001" };
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal("2611001", saved.Value.PartijNumber);
        Assert.All(saved.Value.Lines, l => Assert.Equal(saved.Value.PartijId, l.PartijId));
        Assert.Contains(saved.Value.Changes.Single().Fields, f => f.Field == "partij" && f.Old == null && f.New == "2611001");

        var remove = PurchaseOrderEditHarness.EditOf(saved.Value);
        remove.PartijId = null;
        Assert.Contains("partijId", (await _h.RunAsync(s => s.UpdateAsync(order.Id, remove))).Errors.Keys);
    }
}
