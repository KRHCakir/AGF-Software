using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Artikel verwijderen (alleen zonder enig gebruik), anders archiveren en terugzetten; historie met de volledige naam; guard-regel 19
// houdt ook andere writers tegen (ook de oude tabellen die in de database anders stil zouden meeverdwijnen).
public sealed class ArticleLifecycleServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly ArticleActionDto Jan = new() { ActorReference = " Jan " };

    private async Task<int> NewVariantAsync(string product = "Kiwi", string name = "Hayward", string brand = null)
    {
        await using var db = _factory.CreateContext();
        var existing = await db.Products.SingleOrDefaultAsync(p => p.Name == product);
        var variant = new ProductVariant
        {
            Product = existing ?? new Product { Name = product }, Name = name, Origin = "Nieuw-Zeeland", OriginCountryCode = "NZ", Brand = brand, DefaultPrice = 0m,
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private async Task<List<ArticleAuditEvent>> EventsAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.ArticleAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task An_Unused_Article_Is_Deleted_And_Its_Last_Product_Kind_With_It_History_Keeps_The_Full_Name()
    {
        var id = await NewVariantAsync();
        await using (var db = _factory.CreateContext())
        {
            var service = new ArticleLifecycleService(db);
            Assert.True((await service.UsageAsync(id)).Value.CanDelete);
            Assert.Equal(ServiceResultStatus.Ok, (await service.DeleteAsync(id, Jan)).Status);
        }
        await using (var verify = _factory.CreateContext())
        {
            Assert.False(await verify.ProductVariants.AnyAsync());
            Assert.False(await verify.Products.AnyAsync());
        }
        var events = await EventsAsync();
        Assert.Equal(new[] { "Deleted", "ProductDeleted" }, events.Select(e => e.EventType));
        Assert.Equal("Kiwi Hayward · Nieuw-Zeeland · maat geen · verpakking geen · klasse geen · merk geen", events[0].Description);
        Assert.Equal((id, "Jan"), (events[0].ProductVariantId.Value, events[0].ActorReference));
        Assert.Equal("Kiwi", events[1].Description);

        await using var again = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new ArticleLifecycleService(again).DeleteAsync(id, Jan)).Status);
    }

    [Fact]
    public async Task Deleting_One_Of_Two_Articles_Keeps_The_Product_Kind()
    {
        var first = await NewVariantAsync();
        await NewVariantAsync(name: "Gold");
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new ArticleLifecycleService(db).DeleteAsync(first, Jan)).Status);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal("Gold", (await verify.ProductVariants.SingleAsync()).Name);
        Assert.Equal("Kiwi", (await verify.Products.SingleAsync()).Name);
        Assert.Equal(new[] { "Deleted" }, (await EventsAsync()).Select(e => e.EventType));
    }

    [Fact]
    public async Task Orders_Receipts_And_Old_Orders_Are_Counted_And_Block_Deleting()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await SalesTestData.SeedOrderAsync(_factory, seed);
        await using var db = _factory.CreateContext();
        var service = new ArticleLifecycleService(db);

        var usage = (await service.UsageAsync(seed.AvocadoId)).Value;
        Assert.False(usage.CanDelete);
        var kinds = usage.Usages.Select(u => u.Kind).ToList();
        Assert.Equal(new[] { "purchaseOrderLines", "receipts", "salesOrderLines", "legacyOrderLines" }, kinds);
        Assert.Equal(2, usage.Usages.Single(u => u.Kind == "salesOrderLines").Count);
        Assert.StartsWith("Dit artikel is gebruikt in ", usage.Message);
        Assert.EndsWith(" en kan niet verwijderd worden. Archiveer het in plaats daarvan.", usage.Message);

        var refused = await service.DeleteAsync(seed.AvocadoId, Jan);
        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        Assert.Equal(usage.Message, refused.Message);
        Assert.True(await db.ProductVariants.AnyAsync(v => v.Id == seed.AvocadoId));
        Assert.Empty(await EventsAsync());
    }

    public static IEnumerable<object[]> OtherReferences => new[] { "inventory", "wasteLogs", "purchaseInvoiceLines", "invoiceLines" }.Select(kind => new object[] { kind });

    // Eén verwijzing per soort; de oude voorraad, de afvalregistratie en de oude bestelregels zouden zonder controle stil meeverdwijnen
    // (cascade in de database): ook een directe Remove buiten de service wordt door guard-regel 19 tegengehouden.
    [Theory]
    [MemberData(nameof(OtherReferences))]
    public async Task Every_Other_Reference_Blocks_Deleting_Also_Outside_The_Service(string kind)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var id = await NewVariantAsync();
        await using (var db = _factory.CreateContext())
        {
            switch (kind)
            {
                case "inventory":
                    db.Inventory.Add(new Inventory { ProductVariantId = id, Location = "OUD-1", Quantity = 3 });
                    break;
                case "wasteLogs":
                    var inventory = new Inventory { ProductVariantId = id, Location = "OUD-2", Quantity = 3 };
                    db.WasteLogs.Add(new WasteLog { Inventory = inventory, ProductVariantId = id, QuantityDumped = 1, Reason = "rot" });
                    break;
                case "purchaseInvoiceLines":
                    var incoming = new IncomingInvoice { InvoiceNumber = "SUP-9", InvoiceDate = DateTime.UtcNow, TotalAmount = 10m, Status = "Draft", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                    db.IncomingInvoiceLines.Add(new IncomingInvoiceLine { IncomingInvoice = incoming, ProductVariantId = id, Quantity = 1m, UnitPrice = 10m, LineTotal = 10m });
                    break;
                default:
                    var invoice = new Invoice { CustomerId = seed.CustomerId, InvoiceNumber = "INV-9", InvoiceDate = DateTime.UtcNow, TotalAmount = 10m, PaymentStatus = "Open" };
                    db.Add(new InvoiceLine { Invoice = invoice, ProductVariantId = id, Description = "Kiwi", Quantity = 1m });
                    break;
            }
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            var usage = (await new ArticleLifecycleService(db).UsageAsync(id)).Value;
            Assert.Contains(kind, usage.Usages.Select(u => u.Kind));
            Assert.Equal(ServiceResultStatus.Conflict, (await new ArticleLifecycleService(db).DeleteAsync(id, Jan)).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            db.ProductVariants.Remove(await db.ProductVariants.SingleAsync(v => v.Id == id));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using var verify = _factory.CreateContext();
        Assert.True(await verify.ProductVariants.AnyAsync(v => v.Id == id));
    }

    [Fact]
    public async Task Archive_And_Restore_Write_History_Once_And_An_Archived_Article_Is_Refused_On_New_Lines()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await using (var db = _factory.CreateContext())
        {
            var service = new ArticleLifecycleService(db);
            var archived = (await service.ArchiveAsync(seed.MangoId, Jan)).Value;
            Assert.True(archived.IsArchived);
            Assert.Equal("Jan", archived.ArchivedBy);
            Assert.NotNull(archived.ArchivedAt);
            Assert.True((await service.ArchiveAsync(seed.MangoId, Jan)).Value.IsArchived);
        }
        Assert.Equal(new[] { "Archived" }, (await EventsAsync()).Select(e => e.EventType));

        // Nieuwe regels: een duidelijke melding van de service, en de guard als vangnet voor andere writers.
        await using (var db = _factory.CreateContext())
        {
            var supply = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
            {
                RequestId = Guid.NewGuid(), VendorId = (await db.Vendors.FirstAsync()).Id, Route = "Warehouse", Origin = "Peru", ExpectedAt = PurchaseOrderTestData.ExpectedAt,
                Lines = new List<CreateSupplyCommitmentLineDto> { new() { ProductVariantId = seed.MangoId, ExpectedUnit = "box", ExpectedQuantity = 5m } }
            });
            Assert.NotEqual(ServiceResultStatus.Created, supply.Status);
            Assert.Contains("archived", supply.Message ?? string.Join(" ", supply.Errors?.SelectMany(e => e.Value) ?? Array.Empty<string>()));
        }
        await using (var db = _factory.CreateContext())
        {
            var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "office-1" };
            db.AddRange(order, new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.MangoId, Quantity = 1m, Unit = "box" });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        await using (var db = _factory.CreateContext())
        {
            var restored = (await new ArticleLifecycleService(db).RestoreAsync(seed.MangoId, Jan)).Value;
            Assert.False(restored.IsArchived);
            Assert.Null(restored.ArchivedAt);
            Assert.Null(restored.ArchivedBy);
        }
        Assert.Equal(new[] { "Archived", "Restored" }, (await EventsAsync()).Select(e => e.EventType));
        await using (var db = _factory.CreateContext())
        {
            var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "office-1" };
            db.AddRange(order, new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.MangoId, Quantity = 1m, Unit = "box" });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_Product_Kind_Is_Only_Deleted_Without_Articles()
    {
        var id = await NewVariantAsync();
        await using (var db = _factory.CreateContext())
        {
            db.Products.Add(new Product { Name = "Lychee" });
            await db.SaveChangesAsync();
        }
        await using var service = _factory.CreateContext();
        var lifecycle = new ArticleLifecycleService(service);
        var empty = Assert.Single(await lifecycle.EmptyProductsAsync());
        Assert.Equal("Lychee", empty.Name);
        var kiwi = (await service.Products.SingleAsync(p => p.Name == "Kiwi")).Id;
        var refused = await lifecycle.DeleteProductAsync(kiwi, Jan);
        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        Assert.Contains("1 artikel", refused.Message);
        Assert.Equal(ServiceResultStatus.Ok, (await lifecycle.DeleteProductAsync(empty.Id, Jan)).Status);
        Assert.Empty(await lifecycle.EmptyProductsAsync());
        Assert.True(await service.ProductVariants.AnyAsync(v => v.Id == id));
    }

    [Fact]
    public async Task Article_History_Is_Append_Only()
    {
        var id = await NewVariantAsync();
        await using (var db = _factory.CreateContext())
        {
            await new ArticleLifecycleService(db).ArchiveAsync(id, Jan);
        }
        await using var change = _factory.CreateContext();
        var stored = await change.ArticleAuditEvents.SingleAsync();
        stored.ActorReference = "Piet";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => change.SaveChangesAsync());
    }

    [Fact]
    public void The_Reason_Uses_Singular_And_Plural_In_Plain_Dutch()
    {
        var message = ArticleLifecycleService.InUseMessage(new List<ArticleUsageCountDto>
        {
            new() { Kind = "receipts", Count = 3 }, new() { Kind = "purchaseOrderLines", Count = 1 }, new() { Kind = "wasteLogs", Count = 2 },
        });
        Assert.Equal("Dit artikel is gebruikt in 3 ontvangsten, 1 inkooporderregel en 2 afvalregistraties en kan niet verwijderd worden. Archiveer het in plaats daarvan.", message);
    }
}
