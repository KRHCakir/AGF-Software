using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: de leveringsconditie per inkooporder. Voorgevuld uit de leverancier, aan te passen met Aanpassen (Edit-auditregel met
// oud en nieuw; regels 29 en 37), de leverancier verandert niet.
public sealed class PurchaseOrderDeliveryTermTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_new_purchase_order_takes_the_term_of_the_supplier_and_an_edit_changes_it_with_an_audit_field()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        await using (var db = _factory.CreateContext())
        {
            var vendor = await db.Vendors.SingleAsync(v => v.Id == seed.VendorId);
            (vendor.DeliveryTermKind, vendor.Incoterm, vendor.IncotermPlace) = ("Incoterm", "CIF", "Rotterdam");
            await db.SaveChangesAsync();
        }
        var harness = new PurchaseOrderEditHarness(_factory);
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var detail = await harness.DetailAsync(created.Id);
        Assert.Equal(("CIF", "Rotterdam"), (detail.DeliveryTerm.Incoterm, detail.DeliveryTerm.IncotermPlace));

        // Aanpassen zonder conditie in de aanvraag laat haar staan; met een conditie wordt ze aangepast, met oud en nieuw in de auditregel.
        var untouched = PurchaseOrderEditHarness.EditOf(detail);
        untouched.Note = "Bel bij aankomst";
        Assert.Equal("CIF", (await harness.RunAsync(s => s.UpdateAsync(created.Id, untouched))).Value.DeliveryTerm.Incoterm);
        var edit = PurchaseOrderEditHarness.EditOf(await harness.DetailAsync(created.Id));
        edit.DeliveryTerm = new DeliveryTermDto { DeliveryTermKind = "Franco", DeliveryTermText = "Venlo" };
        var saved = await harness.RunAsync(s => s.UpdateAsync(created.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal(("Franco", "Venlo", (string)null), (saved.Value.DeliveryTerm.DeliveryTermKind, saved.Value.DeliveryTerm.DeliveryTermText, saved.Value.DeliveryTerm.Incoterm));
        Assert.Contains(saved.Value.Changes, c => c.Fields.Any(f => f.Field == "deliveryTerm" && f.Old == "Incoterm CIF Rotterdam" && f.New == "Franco: Venlo"));

        await using var check = _factory.CreateContext();
        Assert.Equal("CIF", (await check.Vendors.AsNoTracking().SingleAsync(v => v.Id == seed.VendorId)).Incoterm);
        var stored = await check.SupplyCommitments.SingleAsync(c => c.Id == created.Id);
        stored.DeliveryTermText = "Venray";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => check.SaveChangesAsync());
    }
}
