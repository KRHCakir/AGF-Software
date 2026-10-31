using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Models;

// Datamodel voor archiveren, idempotent aanmaken en de artikelhistorie (migratie AddArticleLifecycle): de database zelf bewaakt
// dat een gearchiveerd artikel een moment heeft, dat een RequestId maar één artikel maakt en dat een historie-event een doel heeft.
public class ArticleLifecycleModelTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static ProductVariant Hass(Product product, Guid? requestId = null) =>
        new() { Product = product, Name = "Hass", Origin = "Peru", Packaging = "4 kg doos", RequestId = requestId };

    private async Task AssertSaveIsRejectedAsync(Action<AgfAdmin.Data.AppDbContext> arrange)
    {
        await using var db = _factory.CreateContext();
        arrange(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task New_Variants_Are_Active_And_An_Archived_Variant_Needs_A_Moment()
    {
        await using (var db = _factory.CreateContext())
        {
            db.ProductVariants.Add(Hass(new Product { Name = "Avocado" }));
            await db.SaveChangesAsync();
        }
        await using (var verify = _factory.CreateContext())
        {
            var variant = await verify.ProductVariants.SingleAsync();
            Assert.False(variant.IsArchived);
            Assert.Null(variant.ArchivedAt);
        }

        await AssertSaveIsRejectedAsync(db =>
        {
            var withoutMoment = Hass(new Product { Name = "Mango" });
            withoutMoment.IsArchived = true;
            db.ProductVariants.Add(withoutMoment);
        });

        await using var ok = _factory.CreateContext();
        var archived = Hass(new Product { Name = "Citroen" });
        archived.IsArchived = true;
        archived.ArchivedAt = DateTime.UtcNow;
        archived.ArchivedBy = "Jan";
        ok.ProductVariants.Add(archived);
        await ok.SaveChangesAsync();
    }

    [Fact]
    public async Task A_RequestId_Creates_At_Most_One_Variant_And_Variants_Without_One_Are_Unlimited()
    {
        var requestId = Guid.NewGuid();
        await using (var db = _factory.CreateContext())
        {
            var product = new Product { Name = "Avocado" };
            db.ProductVariants.AddRange(Hass(product, requestId), Hass(product), Hass(product));
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db => db.ProductVariants.Add(Hass(new Product { Name = "Mango" }, requestId)));
    }

    [Fact]
    public async Task An_Audit_Event_Needs_A_Variant_Or_Product_And_A_Unique_RequestId()
    {
        var requestId = Guid.NewGuid();
        ArticleAuditEvent Event(int? variantId, int? productId, Guid? id = null) => new()
        {
            ProductVariantId = variantId, ProductId = productId, EventType = "Archived", Description = "Avocado Hass · Peru", OccurredAt = DateTime.UtcNow, RequestId = id,
        };

        await using (var db = _factory.CreateContext())
        {
            // Zonder foreign key: het doel mag later verwijderd zijn.
            db.ArticleAuditEvents.AddRange(Event(999, null, requestId), Event(null, 998), Event(999, 998));
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db => db.ArticleAuditEvents.Add(Event(null, null)));
        await AssertSaveIsRejectedAsync(db => db.ArticleAuditEvents.Add(Event(1, null, requestId)));
    }
}
