using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2, regel 24: een verkoop-partij hoort bij precies één verkooporder en elke order heeft er precies één; soort en order veranderen
// nooit, een verkoop-partij verdwijnt nooit, en een inkooporder krijgt nooit een verkoop-partij. Regel 23 blijft voor inkoop-partijen gelijk.
public sealed class SalesPartijGuardTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public SalesPartijGuardTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task<ServiceResult<PartijSummaryDto>> PurchaseAsync(string number = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number });
    }

    private async Task<SalesOrderDto> SaleAsync() => (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;

    [Fact]
    public async Task Every_Sales_Order_Gets_Exactly_One_Sales_Lot_Next_To_Its_Order_Number()
    {
        await _s.ArrangeAsync();
        var order = await SaleAsync();

        await using var db = _s.Factory.CreateContext();
        var partij = await db.Partijen.SingleAsync(p => p.Kind == "Sales");
        Assert.Equal(("VO-2026-0001", "2611001", order.Id), (order.OrderNumber, partij.Number, partij.SalesOrderId.Value));
        Assert.False(string.IsNullOrWhiteSpace(partij.CreatedBy));

        // Regel 24: geen tweede verkoop-partij bij een bestaande order, geen andere soort of order, nooit verwijderen.
        db.Partijen.Add(new Partij { Kind = "Sales", Number = "999", NumberKey = "999", CreatedBy = "x", SalesOrderId = order.Id });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var stored = await db.Partijen.SingleAsync(p => p.Id == partij.Id);
        stored.Kind = "Purchase";
        stored.SalesOrderId = null;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Partijen.Remove(await db.Partijen.SingleAsync(p => p.Id == partij.Id));
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(ServiceResultStatus.Conflict, (await new PartijService(db).DeleteAsync(partij.Id)).Status);
    }

    [Fact]
    public async Task A_Purchase_Order_Never_Gets_A_Sales_Lot()
    {
        await _s.ArrangeAsync();
        var sale = await SaleAsync();
        await using var db = _s.Factory.CreateContext();
        var salesLot = await db.Partijen.Where(p => p.SalesOrderId == sale.Id).Select(p => p.Id).SingleAsync();
        var link = await new PartijService(db).LinkOrderAsync(salesLot, new LinkPartijDto { SupplyCommitmentId = await db.SupplyCommitments.Select(c => c.Id).FirstAsync() });
        Assert.Equal(ServiceResultStatus.Validation, link.Status);

        var order = await db.SupplyCommitments.Include(c => c.Lines).FirstAsync();
        order.PartijId = salesLot;
        foreach (var line in order.Lines) line.PartijId = salesLot;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_Purchase_Lot_Never_Changes_Its_Number_Without_A_Reason()
    {
        await _s.ArrangeAsync();
        var purchaseId = (await PurchaseAsync()).Value.Id;
        await using var db = _s.Factory.CreateContext();
        var purchase = await db.Partijen.SingleAsync(p => p.Id == purchaseId);
        Assert.Equal(ServiceResultStatus.Validation, (await new PartijService(db).CorrectNumberAsync(purchaseId, new CorrectPartijNumberDto { Number = "X1" })).Status);
        db.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = purchaseId, EventType = "NumberChanged", OldValue = purchase.Number, NewValue = "X1", ActorReference = "x" });
        purchase.Number = "X1";
        purchase.NumberKey = "X1";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
