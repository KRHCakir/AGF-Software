using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, factureren vanuit de levering: een concept neemt prijs, korting en btw van de orderregel over, dienst- en
// emballageregels gaan mee, een deelfactuur laat een rest, en er wordt nooit meer gefactureerd dan geleverd (regel 45).
public sealed class SalesInvoiceDeliveryTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public SalesInvoiceDeliveryTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 25));

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<SalesInvoiceDto>> UpdateAsync(SalesInvoiceDto invoice, params UpdateSalesInvoiceLineDto[] lines)
    {
        await using var db = _factory.CreateContext();
        return await new SalesInvoiceService(db).UpdateAsync(invoice.Id, new UpdateSalesInvoiceDto
        {
            ExpectedVersion = invoice.Version, ReferenceNumber = invoice.ReferenceNumber, Lines = lines.ToList()
        });
    }

    private static UpdateSalesInvoiceLineDto Keep(SalesInvoiceLineDto line, decimal? quantity = null, decimal? price = null, decimal? vat = null) => new()
    {
        Id = line.Id, Quantity = quantity ?? line.Quantity, UnitPrice = price ?? line.UnitPrice, DiscountPercentage = line.DiscountPercentage, VatPercentage = vat ?? line.VatPercentage
    };

    private async Task<List<DeliveryInvoicingDto>> InvoicingAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesInvoiceService(db).OrderInvoicingAsync(orderId)).Value;
    }

    [Fact]
    public async Task A_Concept_From_A_Delivery_Takes_Price_Discount_And_Vat_From_The_Order_With_Service_And_Packaging()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var result = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var invoice = result.Value;
        Assert.Equal(SalesInvoiceRules.StatusConcept, invoice.Status);
        Assert.Equal(SalesInvoiceRules.KindDelivery, invoice.Kind);
        Assert.Null(invoice.InvoiceNumber);
        Assert.Equal("Customer T", invoice.CustomerName);
        Assert.Equal("PO-778", invoice.ReferenceNumber);
        Assert.NotNull(invoice.DeliveryDate);
        Assert.Equal(new[] { SalesInvoiceRules.LineProduct, SalesInvoiceRules.LineService, SalesInvoiceRules.LinePackaging }, invoice.Lines.Select(l => l.LineKind));

        var goods = invoice.Lines[0];
        Assert.Equal((30m, "box", 12.50m, 10m, 9m), (goods.Quantity, goods.Unit, goods.UnitPrice.Value, goods.DiscountPercentage, goods.VatPercentage));
        Assert.Equal(337.50m, goods.Amount);
        Assert.Equal(30.38m, goods.VatAmount);
        Assert.Equal(120m, goods.NetWeightKg);
        Assert.Equal("LOT-A", goods.LotNumber);
        Assert.Equal(30m, goods.MaxQuantity);
        Assert.StartsWith("Avocado Hass, 4kg Box", goods.Description);
        Assert.Equal(a.FirstDelivery, goods.SalesDeliveryId);

        var handling = invoice.Lines[1];
        Assert.Equal(("Handling", 1m, 25m, 21m), (handling.Description, handling.Quantity, handling.UnitPrice.Value, handling.VatPercentage));
        Assert.Equal("Omzet diensten", handling.RevenueTypeName);
        var pallets = invoice.Lines[2];
        Assert.Equal(("Pallet wegwerp", 2m, 15.00m), (pallets.Description, pallets.Quantity, pallets.Amount.Value));

        // Btw per tarief apart: fruit 9%, handling en emballage 21%.
        Assert.Equal(new[] { (9m, 337.50m, 30.38m), (21m, 40.00m, 8.40m) }, invoice.Totals.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal((377.50m, 38.78m, 416.28m), (invoice.Totals.Net.Value, invoice.Totals.Vat.Value, invoice.Totals.Gross.Value));

        await using var db = _factory.CreateContext();
        var stored = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal((377.50m, 38.78m, 416.28m), (stored.SubTotal, stored.VATAmount, stored.TotalAmount));
    }

    [Fact]
    public async Task A_Partial_Invoice_Leaves_A_Rest_That_A_Second_Invoice_Takes_And_Then_Nothing_Is_Left()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var first = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        var partial = await UpdateAsync(first, Keep(first.Lines[0], quantity: 20m), Keep(first.Lines[1]), Keep(first.Lines[2]));
        Assert.Equal(ServiceResultStatus.Ok, partial.Status);
        Assert.Equal(80m, partial.Value.Lines[0].NetWeightKg);

        var invoicing = (await InvoicingAsync(a.OrderId)).Single(d => d.SalesDeliveryId == a.FirstDelivery);
        Assert.Equal((30m, 20m, 10m), (invoicing.Lines.Single().Delivered, invoicing.Lines.Single().Invoiced, invoicing.Lines.Single().Rest));
        Assert.True(invoicing.HasRest);
        Assert.Equal(first.Id, invoicing.OpenConceptId);

        var second = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery);
        Assert.Equal(ServiceResultStatus.Created, second.Status);
        Assert.Equal(10m, second.Value.Lines.Single().Quantity);
        Assert.Equal(40m, second.Value.Lines.Single().NetWeightKg);

        var none = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery);
        Assert.Equal(ServiceResultStatus.Conflict, none.Status);
        Assert.Contains("nothing left to invoice", none.Message);
    }

    [Fact]
    public async Task Never_More_Than_Delivered_Not_Via_The_Service_And_Not_Via_The_Database()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;

        var tooMuch = await UpdateAsync(invoice, Keep(invoice.Lines[0], quantity: 31m));
        Assert.Equal(ServiceResultStatus.Validation, tooMuch.Status);
        Assert.Contains("never more than delivered", tooMuch.Errors["lines[0].quantity"][0]);

        await using var db = _factory.CreateContext();
        var other = new Invoice
        {
            Kind = SalesInvoiceRules.KindDelivery, Status = SalesInvoiceRules.StatusConcept, CustomerId = a.Seed.CustomerId, RequestId = Guid.NewGuid(), CreatedBy = "test",
            InvoiceLines = new List<InvoiceLine>
            {
                new() { LineKind = SalesInvoiceRules.LineProduct, SalesDeliveryId = a.FirstDelivery, SalesOrderLineId = a.BoxLine, ProductVariantId = a.Seed.AvocadoId,
                    Description = "Avocado", Quantity = 1m, Unit = "box", VATPercentage = 9m }
            }
        };
        db.Invoices.Add(other);
        await Assert.ThrowsAsync<InvoiceQuantityConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_Deliveries_Of_One_Customer_Go_On_One_Invoice_And_A_Delivery_Of_Another_Customer_Is_Refused()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var both = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery, a.SecondDelivery);
        Assert.Equal(ServiceResultStatus.Created, both.Status);
        Assert.Equal(new[] { (a.FirstDelivery, 30m), (a.SecondDelivery, 15m) },
            both.Value.Lines.Where(l => l.LineKind == SalesInvoiceRules.LineProduct).Select(l => (l.SalesDeliveryId.Value, l.Quantity)));
        Assert.Equal(new[] { 1, 2 }, both.Value.Deliveries.Select(d => d.Sequence));
        Assert.Equal("LOT-C", both.Value.Lines[1].LotNumber);

        // Een order van een andere klant, geleverd: 5 colli.
        int otherDelivery;
        await using (var db = _factory.CreateContext())
        {
            var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = a.Seed.OtherCustomerId, Status = "Open", RequestedDeliveryDate = new DateTime(2026, 11, 21, 6, 0, 0, DateTimeKind.Utc), ActorReference = "office-1" };
            var colli = new SalesOrderLine { SalesOrder = order, ProductVariantId = a.Seed.AvocadoId, Quantity = 5m, Unit = "colli", UnitPrice = 3m, VatPercentage = 9m };
            db.AddRange(order, colli);
            await db.SaveChangesAsync();
            var reservation = await SalesInvoiceTestData.ReserveAsync(_factory, order.Id, colli.Id, a.Seed.ApprovedColli, 5m, 27.5m, "colli");
            await SalesTestData.ConsumeAsync(_factory, reservation);
            otherDelivery = await db.SalesDeliveries.Where(d => d.SalesOrderId == order.Id).Select(d => d.Id).SingleAsync();
        }
        var mixed = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery, otherDelivery);
        Assert.Equal(ServiceResultStatus.Validation, mixed.Status);
        Assert.Contains("another customer", mixed.Errors["deliveryIds"][0]);

        await using var add = _factory.CreateContext();
        var added = await new SalesInvoiceService(add).AddDeliveriesAsync(both.Value.Id, new AddInvoiceDeliveriesDto { ExpectedVersion = both.Value.Version, DeliveryIds = new List<int> { otherDelivery } });
        Assert.Equal(ServiceResultStatus.Validation, added.Status);
    }

    [Fact]
    public async Task A_Delivery_Added_Later_Joins_The_Concept_And_A_Repeated_Request_Returns_The_Same_Concept()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var key = Guid.NewGuid();
        var first = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, key, a.FirstDelivery)).Value;
        var replay = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, key, a.FirstDelivery);
        Assert.Equal(ServiceResultStatus.Ok, replay.Status);
        Assert.Equal(first.Id, replay.Value.Id);
        Assert.Equal(ServiceResultStatus.Conflict, (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, key, a.SecondDelivery)).Status);

        await using var db = _factory.CreateContext();
        var added = await new SalesInvoiceService(db).AddDeliveriesAsync(first.Id, new AddInvoiceDeliveriesDto { ExpectedVersion = first.Version, DeliveryIds = new List<int> { a.SecondDelivery } });
        Assert.Equal(ServiceResultStatus.Ok, added.Status);
        Assert.Equal(new[] { 30m, 15m }, added.Value.Lines.Where(l => l.LineKind == SalesInvoiceRules.LineProduct).Select(l => l.Quantity));
        Assert.Equal(2, added.Value.Lines.Count(l => l.LineKind != SalesInvoiceRules.LineProduct));
    }

    [Fact]
    public async Task A_Discarded_Concept_Or_A_Removed_Line_Frees_The_Delivery_Again()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        var removed = await UpdateAsync(concept, new UpdateSalesInvoiceLineDto { Id = concept.Lines[0].Id, Remove = true }, Keep(concept.Lines[1]), Keep(concept.Lines[2]));
        Assert.Equal(ServiceResultStatus.Ok, removed.Status);
        Assert.Equal(2, removed.Value.Lines.Count);
        Assert.Equal(30m, (await InvoicingAsync(a.OrderId)).Single(d => d.SalesDeliveryId == a.FirstDelivery).Lines.Single().Rest);

        await using (var db = _factory.CreateContext())
        {
            var discarded = await new SalesInvoiceService(db).DiscardAsync(concept.Id, new DiscardSalesInvoiceDto { ExpectedVersion = removed.Value.Version, Reason = "Verkeerde klant" });
            Assert.Equal(ServiceResultStatus.Ok, discarded.Status);
            Assert.Equal(SalesInvoiceRules.StatusDiscarded, discarded.Value.Status);
            Assert.Equal("Verkeerde klant", discarded.Value.DiscardReason);
        }
        var again = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery);
        Assert.Equal(ServiceResultStatus.Created, again.Status);
        Assert.Equal(3, again.Value.Lines.Count);
    }

    [Fact]
    public async Task A_Change_On_An_Old_Version_Is_Refused_With_Reload()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(concept, Keep(concept.Lines[0], price: 13m))).Status);
        var stale = await UpdateAsync(concept, Keep(concept.Lines[0], price: 14m));
        Assert.Equal(ServiceResultStatus.Conflict, stale.Status);
        Assert.Contains("Reload", stale.Message);
    }

    [Fact]
    public async Task The_Worklist_Shows_Deliveries_With_A_Rest_Per_Customer_With_Days_Since_Delivery()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory, shipSecond: false);
        await using (var db = _factory.CreateContext())
        {
            var list = await new SalesInvoiceService(db).ToInvoiceAsync();
            var customer = Assert.Single(list);
            Assert.Equal("Customer T", customer.CustomerName);
            var delivery = Assert.Single(customer.Deliveries);
            Assert.Equal(a.FirstDelivery, delivery.SalesDeliveryId);
            Assert.NotNull(delivery.DaysSinceDelivery);
            Assert.Null(delivery.OpenConceptId);
        }
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        await using (var db = _factory.CreateContext())
        {
            Assert.Empty(await new SalesInvoiceService(db).ToInvoiceAsync());
            var order = await new SalesInvoiceService(db).OrderInvoicingAsync(a.OrderId);
            Assert.Equal(concept.Id, order.Value.Single(d => d.SalesDeliveryId == a.FirstDelivery).Invoices.Single().Id);
            Assert.False(order.Value.Single(d => d.SalesDeliveryId == a.SecondDelivery).HasRest);
        }
    }

    [Fact]
    public async Task A_Concept_Older_Than_Seven_Days_Is_Marked_Stale()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        Assert.False(concept.Stale);
        _factory.Calendar = new FixedBusinessCalendar(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8));
        await using var db = _factory.CreateContext();
        Assert.True((await new SalesInvoiceService(db).GetAsync(concept.Id)).Value.Stale);
    }

    // Gevonden door de AI-review: regel 45 controleerde ook de andere orderregels van dezelfde levering, zonder hun geleverde aantal te
    // kennen; en de werklijst miste de productnaam in de omschrijving.
    [Fact]
    public async Task Two_Order_Lines_Of_One_Delivery_Go_On_Separate_Invoices_And_The_Worklist_Names_The_Product()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, colli) = await SalesTestData.SeedOrderAsync(_factory, seed, boxQuantity: 10m, colliQuantity: 5m);
        var boxReservation = await SalesInvoiceTestData.ReserveAsync(_factory, orderId, box, seed.ApprovedBox, 10m, 40m);
        var colliReservation = await SalesInvoiceTestData.ReserveAsync(_factory, orderId, colli, seed.ApprovedColli, 5m, 27.5m, "colli");
        await SalesTestData.ConsumeAsync(_factory, boxReservation);
        await SalesTestData.ConsumeAsync(_factory, colliReservation);
        int delivery;
        await using (var db = _factory.CreateContext())
        {
            delivery = await db.SalesDeliveries.Where(d => d.SalesOrderId == orderId).Select(d => d.Id).SingleAsync();
            var worklist = Assert.Single(await new SalesInvoiceService(db).ToInvoiceAsync());
            Assert.All(worklist.Deliveries.Single().Lines, l => Assert.StartsWith("Avocado Hass", l.Description));
        }
        var first = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), delivery)).Value;
        var boxOnly = await UpdateAsync(first, Keep(first.Lines[0]), new UpdateSalesInvoiceLineDto { Id = first.Lines[1].Id, Remove = true });
        Assert.Equal(ServiceResultStatus.Ok, boxOnly.Status);
        var second = await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), delivery);
        Assert.Equal(ServiceResultStatus.Created, second.Status);
        Assert.Equal((colli, 5m), (second.Value.Lines.Single().SalesOrderLineId.Value, second.Value.Lines.Single().Quantity));
    }
}
