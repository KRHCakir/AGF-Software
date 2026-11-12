using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Stap 4a: Definitief maken legt wie en wanneer vast; daarna alleen een correctie met reden (prijs, ordernummer van de leverancier,
// notitie). Een gekoppelde factuur (stap 4b) maakt een order via dezelfde plek definitief; de lijst filtert op documentstatus.
public class PurchaseOrderFinalizeTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public PurchaseOrderFinalizeTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Definitive_Records_Who_And_When_Refuses_Edits_And_Allows_Only_A_Correction_With_Reason()
    {
        var order = await _h.OrderAsync();
        var finalized = await _h.RunAsync(s => s.FinalizeAsync(order.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0 }));
        Assert.Equal(("Definitive", PurchaseOrderEditHarness.Actor, "Manual", false), (finalized.Value.DocumentStatus, finalized.Value.FinalizedBy, finalized.Value.FinalizedVia, finalized.Value.Editable));
        Assert.NotNull(finalized.Value.FinalizedAt);
        Assert.Contains(finalized.Value.History, h => h.Type == "Finalized" && h.ActorReference == PurchaseOrderEditHarness.Actor);

        // Nog eens definitief maken verandert niets; aanpassen wordt geweigerd met de reden.
        var again = await _h.RunAsync(s => s.FinalizeAsync(order.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0 }));
        Assert.Equal((finalized.Value.FinalizedAt, 1), (again.Value.FinalizedAt, again.Value.Changes.Count));
        var edit = PurchaseOrderEditHarness.EditOf(finalized.Value);
        edit.Note = "Na definitief";
        Assert.Equal(PurchaseOrderService.DefinitiveCode, PurchaseOrderEditHarness.CodeOf(await _h.RunAsync(s => s.UpdateAsync(order.Id, edit))));

        CorrectPurchaseOrderDto Correction(string reason) => new()
        {
            RequestId = Guid.NewGuid(), ExpectedVersion = finalized.Value.EditVersion, Reason = reason, SupplierReference = "NP-10", Note = "Prijs volgens factuur",
            Lines = { new CorrectPurchaseOrderLineDto { Id = order.Lines[0].Id, UnitPrice = 9.95m } },
        };
        Assert.Contains("reason", (await _h.RunAsync(s => s.CorrectAsync(order.Id, Correction(" ")))).Errors.Keys);
        var corrected = await _h.RunAsync(s => s.CorrectAsync(order.Id, Correction("Prijsafspraak na levering")));
        Assert.Equal((ServiceResultStatus.Ok, "NP-10", 9.95m), (corrected.Status, corrected.Value.SupplierReference, corrected.Value.Lines[0].UnitPrice));
        var correction = corrected.Value.Changes[^1];
        Assert.Equal(("Correction", "Prijsafspraak na levering", 3), (correction.Kind, correction.Reason, correction.Fields.Count));
        Assert.Contains(corrected.Value.History, h => h.Type == "Corrected" && h.Reason == "Prijsafspraak na levering");

        // Een regel van een andere order: geweigerd, en er verandert niets (ook niet het ordernummer of de notitie uit hetzelfde verzoek).
        var foreign = Correction("Verkeerde regel");
        foreign.ExpectedVersion = corrected.Value.EditVersion;
        foreign.SupplierReference = "NP-11";
        foreign.Lines.Add(new CorrectPurchaseOrderLineDto { Id = 999_999, UnitPrice = 1m });
        Assert.Contains("lines[1]", (await _h.RunAsync(s => s.CorrectAsync(order.Id, foreign))).Errors.Keys);
        Assert.Equal("NP-10", (await _h.DetailAsync(order.Id)).SupplierReference);

        var nothing = Correction("Niets");
        nothing.ExpectedVersion = corrected.Value.EditVersion;
        Assert.Contains("correction", (await _h.RunAsync(s => s.CorrectAsync(order.Id, nothing))).Errors.Keys);

        // Een open order corrigeer je niet: die pas je gewoon aan.
        var open = await _h.OrderAsync();
        Assert.Equal(PurchaseOrderService.OpenCode, PurchaseOrderEditHarness.CodeOf(await _h.RunAsync(s => s.CorrectAsync(open.Id, new CorrectPurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0, Reason = "x" }))));
    }

    [Fact]
    public async Task Linking_An_Invoice_Later_Makes_The_Order_Definitive_Once_And_The_List_Filters_On_Document_Status()
    {
        var invoiced = await _h.OrderAsync();
        var open = await _h.OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            var service = new PurchaseOrderService(db);
            Assert.True(await service.FinalizeForInvoiceAsync(invoiced.Id));
            await db.SaveChangesAsync();
            Assert.False(await new PurchaseOrderService(db).FinalizeForInvoiceAsync(invoiced.Id));
        }
        Assert.Equal(("Definitive", "Invoice"), ((await _h.DetailAsync(invoiced.Id)).DocumentStatus, (await _h.DetailAsync(invoiced.Id)).FinalizedVia));

        await using var read = _factory.CreateContext();
        var definitive = (await new PurchaseOrderService(read).ListAsync(null, "all", null, 1, 50, document: "definitive")).Value;
        Assert.Equal(new[] { invoiced.Id }, definitive.Items.Select(i => i.Id).ToArray());
        Assert.Equal("Definitive", definitive.Items.Single().DocumentStatus);
        var openOnes = (await new PurchaseOrderService(read).ListAsync(null, "all", null, 1, 50, document: "Open")).Value;
        Assert.Equal(new[] { open.Id }, openOnes.Items.Select(i => i.Id).ToArray());
        Assert.Equal(ServiceResultStatus.Validation, (await new PurchaseOrderService(read).ListAsync(null, "all", null, 1, 50, document: "Closed")).Status);
    }
}
