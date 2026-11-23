using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: de leveringsconditie per verkooporder (de inkooporder: PurchaseOrderDeliveryTermTests). Voorgevuld uit de klant, per
// order aan te passen (de klant verandert niet), de waarde van de order wint, en elke wijziging staat in de historie (regel 37). Laadbon en
// transportopdracht tonen haar niet, dus een bestaand document verandert er niet door.
public sealed class SalesOrderDeliveryTermTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    private static DeliveryTermDto Fca(string place) => new() { DeliveryTermKind = "Incoterm", Incoterm = "fca", IncotermPlace = place };

    private async Task CustomerTermAsync(string kind, string incoterm, string place, string text)
    {
        await using var db = _s.Factory.CreateContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == _s.CustomerId);
        (customer.DeliveryTermKind, customer.Incoterm, customer.IncotermPlace, customer.DeliveryTermText) = (kind, incoterm, place, text);
        await db.SaveChangesAsync();
    }

    private async Task<ServiceResult<SalesOrderDto>> UpdateAsync(int orderId, int version, DeliveryTermDto term, Guid? requestId = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new SalesOrderService(db).UpdateDeliveryTermAsync(orderId, new UpdateSalesOrderDeliveryTermDto { RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, DeliveryTerm = term });
    }

    [Fact]
    public async Task A_new_sales_order_takes_the_term_of_the_customer_unless_the_order_says_otherwise_and_the_customer_never_changes()
    {
        await _s.ArrangeAsync();
        await CustomerTermAsync("Incoterm", "DAP", "Rotterdam", null);
        var fromCustomer = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        Assert.Equal(("Incoterm", "DAP", "Rotterdam"), (fromCustomer.DeliveryTerm.DeliveryTermKind, fromCustomer.DeliveryTerm.Incoterm, fromCustomer.DeliveryTerm.IncotermPlace));

        var own = _s.Order((5m, "Stock"));
        own.DeliveryTerm = Fca(" Venlo ");
        var ownResult = (await _s.CreateAsync(own)).Value;
        Assert.Equal(("FCA", "Venlo"), (ownResult.DeliveryTerm.Incoterm, ownResult.DeliveryTerm.IncotermPlace));
        var none = _s.Order((5m, "Stock"));
        none.DeliveryTerm = new DeliveryTermDto();
        Assert.Null((await _s.CreateAsync(none)).Value.DeliveryTerm);

        // De waarde van de order wint: de klant later anders maken verandert de order niet, en de order veranderde de klant niet.
        await CustomerTermAsync("Franco", null, null, "vanaf 10 pallets");
        await using var db = _s.Factory.CreateContext();
        var stored = await db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == fromCustomer.Id);
        Assert.Equal(("Incoterm", "DAP"), (stored.DeliveryTermKind, stored.Incoterm));
        Assert.Equal("Franco", (await db.Customers.AsNoTracking().SingleAsync(c => c.Id == _s.CustomerId)).DeliveryTermKind);

        // Replay: dezelfde aanvraag is dezelfde order; met een andere conditie is het een conflict.
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(own)).Status);
        own.DeliveryTerm = Fca("Venray");
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(own)).Status);
    }

    [Theory]
    [InlineData("Incoterm", "XYZ", "Venlo", null, "deliveryTerm.incoterm")]
    [InlineData("Incoterm", "FCA", "", null, "deliveryTerm.incotermPlace")]
    [InlineData("Other", null, null, "", "deliveryTerm.deliveryTermText")]
    [InlineData("Ex works", null, null, null, "deliveryTerm.deliveryTermKind")]
    public async Task An_incomplete_or_unknown_term_is_refused_like_on_the_relation(string kind, string incoterm, string place, string text, string key)
    {
        await _s.ArrangeAsync();
        var order = _s.Order((5m, "Stock"));
        order.DeliveryTerm = new DeliveryTermDto { DeliveryTermKind = kind, Incoterm = incoterm, IncotermPlace = place, DeliveryTermText = text };
        var result = await _s.CreateAsync(order);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.True(result.Errors.ContainsKey(key), string.Join(", ", result.Errors.Keys));
    }

    [Fact]
    public async Task The_term_of_an_open_sales_order_changes_with_an_audit_event_and_the_existing_loading_slip_stays_as_it_is()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new LoadingSlipService(db).ConfirmAsync(order.Id, new ConfirmSalesOrderDto())).Status);
        }
        int versions;
        int version;
        await using (var db = _s.Factory.CreateContext())
        {
            versions = await db.SalesOrderDocuments.CountAsync(d => d.SalesOrderId == order.Id);
            version = (await db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Version;
        }

        var requestId = Guid.NewGuid();
        var changed = await UpdateAsync(order.Id, version, Fca("Venlo"), requestId);
        Assert.Equal(ServiceResultStatus.Ok, changed.Status);
        Assert.Equal(("FCA", "Venlo"), (changed.Value.DeliveryTerm.Incoterm, changed.Value.DeliveryTerm.IncotermPlace));
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(order.Id, version, Fca("Venlo"), requestId)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await UpdateAsync(order.Id, version, Fca("Venray"))).Status);

        await using (var db = _s.Factory.CreateContext())
        {
            var audit = await db.SalesOrderAuditEvents.AsNoTracking().SingleAsync(e => e.SalesOrderId == order.Id && e.EventType == "DeliveryTermChanged");
            Assert.Equal(((string)null, "Incoterm FCA Venlo"), (audit.OldValue, audit.NewValue));
            Assert.False(string.IsNullOrWhiteSpace(audit.ActorReference));
            Assert.False(await new LoadingSlipService(db).RefreshAsync(order.Id), "the loading slip does not show the term, so no new version");
            Assert.Equal(versions, await db.SalesOrderDocuments.CountAsync(d => d.SalesOrderId == order.Id));

            // Buiten het endpoint om (zonder auditregel): regel 37 weigert het.
            var stored = await db.SalesOrders.SingleAsync(o => o.Id == order.Id);
            stored.IncotermPlace = "Venray";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
