using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Regel 46 en de uitbreiding van regels 5, 6 en 8 (verkoopfactuur 1): een productregel van een directe factuur houdt voorraad vast uit
// haar eigen partij zolang de factuur een concept is; die reservering telt mee in de vrije voorraad; afboeken gaat met een afboekregel
// van de factuur; bij Definitief of Vervallen staat er niets meer actief.
public sealed class InvoiceStockGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Arranged(SalesSeed Seed, int LotId, int OtherLotId, int InvoiceId, int LineId);

    // Partij I-2611001 op pallet P-WH (40 doos, 160 kg) en I-2611002 op P-WH2 (20 doos); een directe factuur (concept) met één productregel
    // van 10 doos uit I-2611001.
    private async Task<Arranged> ArrangeAsync()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await using var db = _factory.CreateContext();
        var lot = new Partij { Number = "I-2611001", NumberKey = PartijRules.NumberKey("I-2611001"), CreatedBy = "test" };
        var other = new Partij { Number = "I-2611002", NumberKey = PartijRules.NumberKey("I-2611002"), CreatedBy = "test" };
        db.Partijen.AddRange(lot, other);
        await db.SaveChangesAsync();
        // Opzet buiten de guard om: de inhoud van de twee pallets hoort bij deze partijen.
        await db.Database.ExecuteSqlAsync($"UPDATE \"PalletContentLines\" SET \"PartijId\" = {lot.Id} WHERE \"Id\" = {seed.ApprovedBox}");
        await db.Database.ExecuteSqlAsync($"UPDATE \"PalletContentLines\" SET \"PartijId\" = {other.Id} WHERE \"Id\" = {seed.SecondPalletApprovedBox}");
        var invoice = new Invoice
        {
            Kind = SalesInvoiceRules.KindDirect, Status = SalesInvoiceRules.StatusConcept, CustomerId = seed.CustomerId, RequestId = Guid.NewGuid(), CreatedBy = "test",
            InvoiceLines = new List<InvoiceLine>
            {
                new() { LineKind = SalesInvoiceRules.LineProduct, ProductVariantId = seed.AvocadoId, PartijId = lot.Id, Description = "Avocado Hass", Quantity = 10m, Unit = "box", UnitPrice = 12m, VATPercentage = 9m }
            }
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return new Arranged(seed, lot.Id, other.Id, invoice.Id, invoice.InvoiceLines.Single().Id);
    }

    private static async Task<StockReservation> ReserveAsync(AppDbContext db, int lineId, int contentId, decimal quantity, decimal kg)
    {
        var reservation = new StockReservation
        {
            RequestId = Guid.NewGuid(), InvoiceLineId = lineId, PalletContentLineId = contentId, SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, contentId),
            Quantity = quantity, Unit = "box", NetWeightKg = kg, Source = ReservationRules.SourcePhysical, Status = ReservationRules.StatusActive
        };
        db.StockReservations.Add(reservation);
        return reservation;
    }

    // Afboeken zoals Definitief dat doet: verbruikt, Dispatch-mutatie, afgeboekt op de inhoud en een afboekregel van de factuur.
    private static async Task ConsumeAsync(AppDbContext db, int reservationId, bool withStockLine = true)
    {
        var reservation = await db.StockReservations.Include(r => r.PalletContentLine).Include(r => r.InvoiceLine).SingleAsync(r => r.Id == reservationId);
        var content = reservation.PalletContentLine;
        reservation.Status = ReservationRules.StatusConsumed;
        reservation.ConsumedAt = DateTime.UtcNow;
        content.DispatchedQuantity += reservation.Quantity;
        content.DispatchedNetWeightKg += reservation.NetWeightKg ?? 0m;
        var movement = new StockMovement
        {
            Type = ReservationRules.MovementTypeDispatch, Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg,
            SupplyCommitmentLineId = content.SupplyCommitmentLineId, PalletContentLine = content, StockReservation = reservation, CreatedAt = DateTime.UtcNow
        };
        db.StockMovements.Add(movement);
        if (withStockLine)
        {
            db.SalesInvoiceStockLines.Add(new SalesInvoiceStockLine
            {
                InvoiceId = reservation.InvoiceLine.InvoiceId, InvoiceLine = reservation.InvoiceLine, StockReservation = reservation, PalletContentLine = content,
                Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg.Value, StockMovement = movement
            });
        }
    }

    private static void Final(Invoice invoice)
    {
        invoice.Status = SalesInvoiceRules.StatusFinal;
        invoice.InvoiceYear = 2026;
        invoice.InvoiceSequence = 1;
        invoice.InvoiceNumber = SalesInvoiceRules.FormatNumber(2026, 1);
        invoice.FinalizedAt = DateTime.UtcNow;
        invoice.FinalizedBy = "test";
        invoice.PaymentTermDays = 14;
        invoice.DueDate = DateTime.UtcNow.Date.AddDays(14);
    }

    [Fact]
    public async Task An_Invoice_Reservation_Holds_Stock_From_Its_Own_Lot_And_Counts_As_Not_Free()
    {
        var a = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 35m, 140m);
            await db.SaveChangesAsync();
        }

        // Een verkooporder kan nu nog maar 5 van de 40 doos krijgen: de reservering van de factuur telt mee (regel 6 en de service).
        var (orderId, boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, a.Seed, boxQuantity: 10m);
        await using (var db = _factory.CreateContext())
        {
            var refused = await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = a.Seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m
            });
            Assert.NotEqual(ServiceResultStatus.Created, refused.Status);
            var options = await new SalesOrderService(db).GetLotOptionsAsync(a.Seed.AvocadoId, "box");
            Assert.Equal(5m, options.Value.Single(o => o.PartijId == a.LotId).AvailableQuantity);
        }
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = boxLine, PalletContentLineId = a.Seed.ApprovedBox, SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, a.Seed.ApprovedBox),
                Quantity = 10m, Unit = "box", NetWeightKg = 40m, Source = ReservationRules.SourcePhysical, Status = ReservationRules.StatusActive
            });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_Reservation_From_Another_Lot_Or_For_A_Line_That_Is_Not_A_Product_Concept_Is_Refused()
    {
        var a = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.SecondPalletApprovedBox, 5m, 20m);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.InvoiceLines.SingleAsync(l => l.Id == a.LineId)).RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 5m, 20m);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Booking_Out_Needs_An_Invoice_Stock_Line_And_Then_The_Invoice_Becomes_Final()
    {
        var a = await ArrangeAsync();
        int reservationId;
        await using (var db = _factory.CreateContext())
        {
            reservationId = (await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 10m, 40m)).Id;
            await db.SaveChangesAsync();
            reservationId = (await db.StockReservations.SingleAsync(r => r.InvoiceLineId == a.LineId)).Id;
        }
        // Definitief terwijl er nog voorraad vastgehouden wordt: geweigerd.
        await using (var db = _factory.CreateContext())
        {
            Final(await db.Invoices.SingleAsync(i => i.Id == a.InvoiceId));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            await ConsumeAsync(db, reservationId, withStockLine: false);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            await ConsumeAsync(db, reservationId);
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            Final(await db.Invoices.SingleAsync(i => i.Id == a.InvoiceId));
            await db.SaveChangesAsync();
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == a.Seed.ApprovedBox);
            Assert.Equal(10m, content.DispatchedQuantity);
            Assert.Equal(1, await db.SalesInvoiceStockLines.CountAsync());
            var stockLine = await db.SalesInvoiceStockLines.SingleAsync();
            stockLine.Quantity = 9m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_Final_Invoice_Has_Exactly_Its_Quantity_Booked_Out()
    {
        var a = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 8m, 32m);
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            await ConsumeAsync(db, (await db.StockReservations.SingleAsync(r => r.InvoiceLineId == a.LineId)).Id);
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            Final(await db.Invoices.SingleAsync(i => i.Id == a.InvoiceId));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_Discarded_Concept_Holds_No_Stock_Anymore()
    {
        var a = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 10m, 40m);
            await db.SaveChangesAsync();
        }
        void Discard(Invoice invoice)
        {
            invoice.Status = SalesInvoiceRules.StatusDiscarded;
            invoice.DiscardedAt = DateTime.UtcNow;
            invoice.DiscardedBy = "test";
        }
        await using (var db = _factory.CreateContext())
        {
            Discard(await db.Invoices.SingleAsync(i => i.Id == a.InvoiceId));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync(r => r.InvoiceLineId == a.LineId);
            reservation.Status = ReservationRules.StatusReleased;
            reservation.ReleasedAt = DateTime.UtcNow;
            reservation.ReleaseReason = "Concept weggegooid";
            Discard(await db.Invoices.SingleAsync(i => i.Id == a.InvoiceId));
            await db.SaveChangesAsync();
        }
        // Na weggooien kan er niets meer bij.
        await using (var db = _factory.CreateContext())
        {
            await ReserveAsync(db, a.LineId, a.Seed.ApprovedBox, 1m, 4m);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
