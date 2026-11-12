using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// Documenten deel 1: bevestigen van een verkooporder maakt de laadbon (versie 1, snapshot, wie en wanneer); verandert de bon daarna,
// dan komt er een nieuwe versie en blijft de oude bewaard. Nooit een prijs, bedrag of btw op de bon.
public class LoadingSlipServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<(SalesSeed Seed, int OrderId, int BoxLine, int ColliLine)> OrderAsync()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, colli) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.FindAsync(seed.AvocadoId);
        variant!.Brand = "Westfalia";
        variant.SizeCategory = "18";
        variant.OriginCountryCode = "PE";
        variant.DefaultPackagesPerPallet = 252m;
        var order = await db.SalesOrders.FindAsync(orderId);
        order!.CustomerReference = "PO-778";
        // Emballage mee op de bon: met een prijs op de order, die nooit op de bon komt.
        var euro = new CostArticle { RequestId = Guid.NewGuid(), Name = "Euro-pallet", NameKey = "euro-pallet", Kind = "Packaging", DefaultUnitPrice = 9.50m, VatPercentage = 21m, CreatedBy = "test", CreatedAt = DateTime.UtcNow };
        db.Add(euro);
        db.Add(new SalesOrderPackagingLine { SalesOrderId = orderId, CostArticle = euro, Quantity = 2, UnitPrice = 9.50m, VatPercentage = 21m });
        await db.SaveChangesAsync();
        return (seed, orderId, box, colli);
    }

    private async Task<T> RunAsync<T>(Func<LoadingSlipService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new LoadingSlipService(db));
    }

    private async Task<List<SalesOrderDocument>> VersionsAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == orderId).OrderBy(d => d.Version).ToListAsync();
    }

    [Fact]
    public async Task Confirming_makes_exactly_one_loading_slip_with_a_snapshot_and_who_and_when()
    {
        var (_, orderId, _, _) = await OrderAsync();
        var load = new DateTime(2026, 11, 13, 6, 30, 0, DateTimeKind.Utc);
        var confirmed = await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto { LoadAt = load, LicensePlate = " 12-ABC-3 " }));
        Assert.Equal(ServiceResultStatus.Ok, confirmed.Status);
        Assert.Equal((CurrentUserName, load, "12-ABC-3", false), (confirmed.Value.ConfirmedBy, confirmed.Value.LoadAt, confirmed.Value.LicensePlate, confirmed.Value.NeedsResend));
        var document = Assert.Single(confirmed.Value.Documents);
        Assert.Equal((1, "Confirmed", true), (document.Version, document.Reason, document.Latest));
        Assert.StartsWith("LB-", document.Number);

        // Nog eens bevestigen verandert niets; de documenten opvragen ook niet.
        var again = await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        Assert.Equal((confirmed.Value.ConfirmedAt, "12-ABC-3"), (again.Value.ConfirmedAt, again.Value.LicensePlate));
        await RunAsync(s => s.DocumentsAsync(orderId));
        var stored = Assert.Single(await VersionsAsync(orderId));

        var slip = LoadingSlipBuilder.Deserialize(stored.SnapshotJson);
        Assert.Equal(("Customer T", "PO-778", "en", true, true), (slip.CustomerName, slip.CustomerReference, slip.Language, slip.ShowCustomer, slip.ShowSupplier));
        // Geen reserveringen: per orderregel één rij, de pallets niet afgerond (50 dozen bij 252 per pallet).
        Assert.Equal(2, slip.Rows.Count);
        var box = slip.Rows[0];
        Assert.Equal((50m, "box", "Avocado", "Hass", "4kg Box", "Westfalia", "18", "PE"), (box.Quantity, box.Unit, box.Description, box.Variety, box.Packaging, box.Brand, box.Size, box.Origin));
        Assert.Equal(50m / 252m, box.Pallets);
        Assert.Equal((60m, PurchaseLineMath.Round2(60m / 252m)), (slip.TotalQuantity, PurchaseLineMath.Round2(slip.TotalPallets!.Value)));
        Assert.Equal(new[] { (2, "Euro-pallet") }, slip.Packaging.Select(p => (p.Quantity, p.Description)));
    }

    [Fact]
    public async Task The_loading_slip_never_holds_a_price_an_amount_or_vat()
    {
        var (_, orderId, _, _) = await OrderAsync();
        await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var json = (await VersionsAsync(orderId)).Single().SnapshotJson;
        foreach (var word in new[] { "price", "amount", "vat", "9.5", "12.5", "€" })
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Reserving_after_confirming_makes_version_2_with_the_pallet_and_supplier_and_keeps_version_1()
    {
        var (seed, orderId, boxLine, _) = await OrderAsync();
        await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        await using (var db = _factory.CreateContext())
        {
            var reserved = await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 30m, Unit = "box", NetWeightKg = 120m });
            Assert.Equal(ServiceResultStatus.Created, reserved.Status);
        }
        Assert.True(await RunAsync(s => s.RefreshAsync(orderId)));
        Assert.False(await RunAsync(s => s.RefreshAsync(orderId)), "the same content never gives another version");

        var versions = await VersionsAsync(orderId);
        Assert.Equal(new[] { (1, "Confirmed"), (2, "Changed") }, versions.Select(v => (v.Version, v.Reason)));
        Assert.Equal(2, LoadingSlipBuilder.Deserialize(versions[0].SnapshotJson).Rows.Count);
        var rows = LoadingSlipBuilder.Deserialize(versions[1].SnapshotJson).Rows;
        Assert.Equal(new[] { (30m, "Supplier B · LOT-A", "P-WH", (decimal?)120m), (20m, (string)null, (string)null, (decimal?)null), (10m, null, null, null) },
            rows.Select(r => (r.Quantity, r.Supplier, r.PalletReference, r.NetKg)));
        Assert.True((await RunAsync(s => s.DocumentsAsync(orderId))).Value.NeedsResend);

        // Doorgegeven: alleen de nieuwste versie, daarna geen melding meer.
        var latest = versions[1];
        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(s => s.MarkSentAsync(orderId, versions[0].Id))).Status);
        var sent = await RunAsync(s => s.MarkSentAsync(orderId, latest.Id));
        Assert.Equal((false, CurrentUserName), (sent.Value.NeedsResend, sent.Value.Documents.Single(d => d.Latest).SentBy));
    }

    [Fact]
    public async Task Changing_loading_time_or_plate_gives_a_new_version_an_unconfirmed_order_has_none()
    {
        var (_, orderId, _, _) = await OrderAsync();
        Assert.False(await RunAsync(s => s.RefreshAsync(orderId)));
        Assert.Empty(await VersionsAsync(orderId));
        Assert.Contains("confirmedAt", (await RunAsync(s => s.UpdateLoadingAsync(orderId, new UpdateSalesOrderLoadingDto { LicensePlate = "X" }))).Errors.Keys);

        await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var updated = await RunAsync(s => s.UpdateLoadingAsync(orderId, new UpdateSalesOrderLoadingDto { LicensePlate = "BX-44-ZZ" }));
        Assert.Equal(("BX-44-ZZ", 2), (updated.Value.LicensePlate, updated.Value.Documents.Max(d => d.Version)));
        Assert.Equal("BX-44-ZZ", LoadingSlipBuilder.Deserialize((await VersionsAsync(orderId)).Last().SnapshotJson).LicensePlate);
        Assert.Contains("licensePlate", (await RunAsync(s => s.UpdateLoadingAsync(orderId, new UpdateSalesOrderLoadingDto { LicensePlate = new string('A', 33) }))).Errors.Keys);
    }

    [Fact]
    public async Task An_order_without_lines_or_a_cancelled_order_is_not_confirmed()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        int empty, cancelled;
        await using (var db = _factory.CreateContext())
        {
            var a = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow.Date, ActorReference = "t" };
            db.Add(a);
            await db.SaveChangesAsync();
            empty = a.Id;
        }
        cancelled = (await SalesTestData.SeedOrderAsync(_factory, seed)).OrderId;
        await using (var db = _factory.CreateContext())
        {
            var order = await db.SalesOrders.FindAsync(cancelled);
            order!.Status = "Cancelled";
            order.CancelledAt = DateTime.UtcNow;
            order.CancelReason = "test";
            order.CancelRequestId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        Assert.Contains("lines", (await RunAsync(s => s.ConfirmAsync(empty, new ConfirmSalesOrderDto()))).Errors.Keys);
        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(s => s.ConfirmAsync(cancelled, new ConfirmSalesOrderDto()))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RunAsync(s => s.ConfirmAsync(999_999, new ConfirmSalesOrderDto()))).Status);
    }

    [Fact]
    public async Task Customer_name_and_supplier_column_follow_the_company_settings()
    {
        var (seed, orderId, boxLine, _) = await OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            var settings = await db.CompanySettings.FirstOrDefaultAsync() ?? db.CompanySettings.Add(new CompanySettings()).Entity;
            settings.LoadingSlipShowCustomer = false;
            settings.LoadingSlipShowSupplier = false;
            await db.SaveChangesAsync();
            await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 30m, Unit = "box", NetWeightKg = 120m });
        }
        await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var slip = LoadingSlipBuilder.Deserialize((await VersionsAsync(orderId)).Single().SnapshotJson);
        Assert.Equal(((string)null, false, false), (slip.CustomerName, slip.ShowCustomer, slip.ShowSupplier));
        Assert.All(slip.Rows, r => Assert.Null(r.Supplier));
        Assert.Equal("P-WH", slip.Rows[0].PalletReference);
    }

    [Fact]
    public async Task The_loading_slip_settings_default_to_english_with_customer_and_supplier_and_refuse_an_unknown_language()
    {
        var settings = await RunAsync(s => s.GetSettingsAsync());
        Assert.Equal(("en", true, true), (settings.Language, settings.ShowCustomer, settings.ShowSupplier));
        Assert.Contains("language", (await RunAsync(s => s.UpdateSettingsAsync(new LoadingSlipSettingsDto { Language = "de" }))).Errors.Keys);
        var updated = await RunAsync(s => s.UpdateSettingsAsync(new LoadingSlipSettingsDto { Language = "NL", ShowCustomer = false, ShowSupplier = true, WarehouseAddress = "  Koelhuis Zuid, Havenweg 4, Ridderkerk " }));
        Assert.Equal(("nl", false, true, "Koelhuis Zuid, Havenweg 4, Ridderkerk"), (updated.Value.Language, updated.Value.ShowCustomer, updated.Value.ShowSupplier, updated.Value.WarehouseAddress));
        Assert.Contains("warehouseAddress", (await RunAsync(s => s.UpdateSettingsAsync(new LoadingSlipSettingsDto { Language = "en", WarehouseAddress = new string('A', 301) }))).Errors.Keys);
        // Leeg = weer het bedrijfsadres.
        Assert.Null((await RunAsync(s => s.UpdateSettingsAsync(new LoadingSlipSettingsDto { Language = "en", WarehouseAddress = " " }))).Value.WarehouseAddress);
    }

    [Fact]
    public async Task Passing_on_an_older_version_is_refused_but_the_new_version_from_the_refresh_is_kept()
    {
        var (seed, orderId, boxLine, _) = await OrderAsync();
        var first = (await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()))).Value.Documents.Single();
        // Een wijziging zonder verversing erna (alsof die nog onderweg is): de verversing bij het doorgeven maakt versie 2.
        await using (var db = _factory.CreateContext())
        {
            await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 30m, Unit = "box", NetWeightKg = 120m });
        }
        var refused = await RunAsync(s => s.MarkSentAsync(orderId, first.Id));
        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        var versions = await VersionsAsync(orderId);
        Assert.Equal(new[] { (1, (string)null), (2, (string)null) }, versions.Select(v => (v.Version, v.SentBy)));
    }

    [Fact]
    public async Task A_change_saved_while_passing_on_gives_a_newer_version_that_asks_to_send_again()
    {
        var (_, orderId, _, _) = await OrderAsync();
        var first = (await RunAsync(s => s.ConfirmAsync(orderId, new ConfirmSalesOrderDto()))).Value.Documents.Single();
        // Een gelijktijdige wijziging van een bestaande emballageregel, opgeslagen na de controle en vlak voor de doorgifte. Die raakt de
        // order en de orderregels niet, dus een lock daarop zou haar niet tegenhouden.
        var change = new BeforeSentUpdate($"UPDATE \"SalesOrderPackagingLines\" SET \"Quantity\" = 5 WHERE \"SalesOrderId\" = {orderId}");
        await using var db = _factory.CreateContext(change);
        var sent = await new LoadingSlipService(db).MarkSentAsync(orderId, first.Id);

        Assert.True(change.Ran);
        Assert.Equal(ServiceResultStatus.Ok, sent.Status);
        Assert.True(sent.Value.NeedsResend, "the version that was passed on is no longer the latest");
        Assert.Equal(new[] { (2, (string)null, true), (1, CurrentUserName, false) }, sent.Value.Documents.Select(d => (d.Version, d.SentBy, d.Latest)));
        Assert.Equal(new[] { (5, "Euro-pallet") }, LoadingSlipBuilder.Deserialize((await VersionsAsync(orderId)).Last().SnapshotJson).Packaging.Select(p => (p.Quantity, p.Description)));
    }

    // Voert één keer een wijziging uit vlak voordat de doorgifte (de update van SentAt) naar de database gaat.
    private sealed class BeforeSentUpdate(string sql) : DbCommandInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ran && command.CommandText.Contains("\"SentAt\"") && command.CommandText.StartsWith("UPDATE \"SalesOrderDocuments\"", StringComparison.Ordinal))
            {
                Ran = true;
                await using var other = command.Connection!.CreateCommand();
                other.Transaction = command.Transaction;
                other.CommandText = sql;
                await other.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private static string CurrentUserName => CurrentUser.Default.Actor(100);
}
