using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Documenten deel 2: de transportopdracht. Ontstaat bij bevestigen als er transport is ingevuld (of later, als transport na bevestigen
// komt), als document met snapshot en versies naast de laadbon. Nooit een prijs, bedrag of tarief.
public class TransportOrderServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private sealed record Setup(SalesSeed Seed, int OrderId, int BoxLine, int ColliLine, int CarrierId, int DeliveryAddressId, int OtherAddressId);

    private async Task<Setup> OrderAsync()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, colli) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.FindAsync(seed.AvocadoId);
        variant!.TradeClass = "I";
        variant.OriginCountryCode = "PE";
        variant.DefaultPackagesPerPallet = 252m;
        variant.DefaultGrossKgPerPackage = 4.3m;
        var order = await db.SalesOrders.Include(o => o.Customer).SingleAsync(o => o.Id == orderId);
        order.CustomerReference = "PO-778";
        var delivery = new RelationAddress { RelationId = order.Customer.RelationId, Purpose = AddressPurpose.Delivery, Label = "DC Noord", Line1 = "Havenweg 4", PostalCode = "9723 AB", City = "Groningen", CountryCode = "NL", IsDefault = true };
        var carrier = new Relation { Name = "Transport Zeeland B.V.", IsCarrier = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        carrier.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Main, Line1 = "Kade 1", PostalCode = "4382 AA", City = "Vlissingen", CountryCode = "NL" });
        var otherRelation = new Relation { Name = "Iemand anders", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        otherRelation.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Delivery, Line1 = "Elders 9", City = "Venlo", CountryCode = "NL" });
        db.AddRange(delivery, carrier, otherRelation);
        var euro = new CostArticle { RequestId = Guid.NewGuid(), Name = "Euro-pallet", NameKey = "euro-pallet", Kind = "Packaging", DefaultUnitPrice = 9.50m, VatPercentage = 21m, CreatedBy = "test", CreatedAt = DateTime.UtcNow };
        db.Add(euro);
        db.Add(new SalesOrderPackagingLine { SalesOrderId = orderId, CostArticle = euro, Quantity = 2, UnitPrice = 9.50m, VatPercentage = 21m });
        await db.SaveChangesAsync();
        return new Setup(seed, orderId, box, colli, carrier.Id, delivery.Id, otherRelation.Addresses.Single().Id);
    }

    private async Task<T> Transport<T>(Func<SalesOrderTransportService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new SalesOrderTransportService(db));
    }

    private async Task<T> Slips<T>(Func<LoadingSlipService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new LoadingSlipService(db));
    }

    private async Task<List<SalesOrderDocument>> VersionsAsync(int orderId, string kind)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == orderId && d.Kind == kind).OrderBy(d => d.Version).ToListAsync();
    }

    private static SaveSalesOrderTransportDto ByCarrier(Setup s, int? version = null) => new()
    {
        ExpectedVersion = version, Mode = "Carrier", CarrierRelationId = s.CarrierId, UnloadingAddressId = s.DeliveryAddressId,
        LoadAt = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc), UnloadDate = new DateTime(2026, 11, 14), TemperatureC = 2m, LoadingReference = "LR-55",
    };

    [Fact]
    public async Task Confirming_with_transport_makes_the_transport_order_next_to_the_loading_slip_without_any_price()
    {
        var s = await OrderAsync();
        Assert.Equal(ServiceResultStatus.Ok, (await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s)))).Status);
        var confirmed = await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto { LoadAt = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc) }));
        Assert.True(confirmed.Value.HasTransport);
        Assert.Equal(new[] { ("LoadingSlip", "LB-", 1, true), ("TransportOrder", "TO-", 1, true) },
            confirmed.Value.Documents.OrderBy(d => d.Kind).Select(d => (d.Kind, d.Number[..3], d.Version, d.Latest)));

        var json = Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder)).SnapshotJson;
        var order = TransportOrderBuilder.Deserialize(json);
        Assert.Equal(("Transport Zeeland B.V.", "Kade 1, 4382 AA Vlissingen, NL", false), (order.CarrierName, order.CarrierAddress, order.OwnTransport));
        Assert.Equal("Customer T, DC Noord, Havenweg 4, 9723 AB Groningen, NL", order.UnloadingPlace);
        Assert.Equal(("PO-778", "LR-55", 2m, "Europallet"), (order.UnloadingReference, order.LoadingReference, order.TemperatureC, order.PalletKind));
        // Een rij per orderregel: 50 dozen en 10 colli bij 252 per pallet (niet afgerond); bruto uit het artikel (4,3 kg per verpakking).
        Assert.Equal(new[] { (50m, "Avocado · Hass · 4kg Box", "I", "PE", (decimal?)(50m / 252m), (decimal?)215m), (10m, "Avocado · Hass · 4kg Box", "I", "PE", 10m / 252m, 43m) },
            order.Rows.Select(r => (r.Quantity, r.Description, r.Class, r.Origin, r.PalletSpaces, r.GrossKg)));
        Assert.Equal((60m, 60m / 252m, 258m), (order.TotalQuantity, order.TotalPalletSpaces, order.TotalGrossKg));
        Assert.Equal(0.24m, PurchaseLineMath.Round2(order.TotalPalletSpaces!.Value));
        Assert.Equal(new[] { (2, "Euro-pallet") }, order.Packaging.Select(p => (p.Quantity, p.Description)));
        foreach (var word in new[] { "price", "amount", "vat", "9.5", "12.5" }) Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_transport_there_is_no_transport_order_until_transport_is_filled_in()
    {
        var s = await OrderAsync();
        var confirmed = await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));
        Assert.Equal((false, "LoadingSlip"), (confirmed.Value.HasTransport, Assert.Single(confirmed.Value.Documents).Kind));
        // Eigen transport na bevestigen: de verversing (filter na de PUT) maakt versie 1.
        Assert.Equal(ServiceResultStatus.Ok, (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "own", CarrierRelationId = s.CarrierId }))).Status);
        Assert.True(await Slips(x => x.RefreshAsync(s.OrderId)));
        var order = TransportOrderBuilder.Deserialize(Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder)).SnapshotJson);
        Assert.Equal((true, (string)null), (order.OwnTransport, order.CarrierName));
        Assert.Equal("Customer T, DC Noord, Havenweg 4, 9723 AB Groningen, NL", order.UnloadingPlace);
        Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip));
    }

    [Fact]
    public async Task A_change_after_confirming_gives_a_new_version_and_asks_to_send_again_per_document()
    {
        var s = await OrderAsync();
        var saved = await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s)));
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto { LoadAt = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc) }));

        // Temperatuur anders: alleen de transportopdracht verandert.
        var colder = ByCarrier(s, saved.Value.Version);
        colder.TemperatureC = 1.5m;
        Assert.Equal(ServiceResultStatus.Ok, (await Transport(t => t.SaveAsync(s.OrderId, colder))).Status);
        Assert.True(await Slips(x => x.RefreshAsync(s.OrderId)));
        Assert.False(await Slips(x => x.RefreshAsync(s.OrderId)), "the same content never gives another version");
        var documents = (await Slips(x => x.DocumentsAsync(s.OrderId))).Value;
        Assert.Equal((false, true), (documents.NeedsResend, documents.TransportOrderNeedsResend));
        Assert.Equal(new[] { ("LoadingSlip", 1, true), ("TransportOrder", 1, false), ("TransportOrder", 2, true) },
            documents.Documents.OrderBy(d => d.Kind).ThenBy(d => d.Version).Select(d => (d.Kind, d.Version, d.Latest)));

        // Laden op anders (op de order): laadbon en transportopdracht krijgen samen een nieuwe versie.
        await Slips(x => x.UpdateLoadingAsync(s.OrderId, new UpdateSalesOrderLoadingDto { LoadAt = new DateTime(2026, 11, 13, 7, 0, 0, DateTimeKind.Utc) }));
        Assert.Equal(2, (await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip)).Count);
        var transport = await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder);
        Assert.Equal((3, new DateTime(2026, 11, 13, 7, 0, 0, DateTimeKind.Utc)), (transport.Count, TransportOrderBuilder.Deserialize(transport[^1].SnapshotJson).LoadAt));

        // Doorgegeven geldt per document: de transportopdracht doorgeven laat de laadbon om opnieuw versturen vragen.
        var sent = await Slips(x => x.MarkSentAsync(s.OrderId, transport[^1].Id));
        Assert.Equal((true, false), (sent.Value.NeedsResend, sent.Value.TransportOrderNeedsResend));
    }

    [Fact]
    public async Task Loading_and_unloading_place_can_be_overwritten_per_order_and_the_gross_follows_an_adjusted_line()
    {
        var s = await OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            var line = await db.SalesOrderLines.FindAsync(s.BoxLine);
            line!.GrossWeightKg = 230m;
            line.WeightSource = LineWeightRules.Entered;
            await db.SaveChangesAsync();
        }
        var request = ByCarrier(s);
        request.LoadingPlace = "Koelhuis Zuid, Havenweg 4, Ridderkerk";
        request.UnloadingPlace = "Groothandel Noord, Laan 3, Groningen (achterom)";
        request.UnloadingReference = "AFL-9";
        request.PalletKind = "Europallet + Corners + Straps";
        var saved = await Transport(t => t.SaveAsync(s.OrderId, request));
        Assert.Equal(("Koelhuis Zuid, Havenweg 4, Ridderkerk", "Customer T, DC Noord, Havenweg 4, 9723 AB Groningen, NL"), (saved.Value.LoadingPlace, saved.Value.DefaultUnloadingPlace));
        Assert.Equal(new[] { "Delivery", "Main" }.Take(saved.Value.UnloadingAddresses.Count), saved.Value.UnloadingAddresses.Select(a => a.Purpose));
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));

        var order = TransportOrderBuilder.Deserialize(Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder)).SnapshotJson);
        Assert.Equal(("Koelhuis Zuid, Havenweg 4, Ridderkerk", "Groothandel Noord, Laan 3, Groningen (achterom)", "AFL-9", "Europallet + Corners + Straps"),
            (order.LoadingPlace, order.UnloadingPlace, order.UnloadingReference, order.PalletKind));
        Assert.Equal(new decimal?[] { 230m, 43m }, order.Rows.Select(r => r.GrossKg));
        Assert.Equal(273m, order.TotalGrossKg);
    }

    [Fact]
    public async Task Invalid_transport_is_refused_with_a_reason_per_field()
    {
        var s = await OrderAsync();
        int customerRelation;
        await using (var db = _factory.CreateContext()) customerRelation = (await db.SalesOrders.Include(o => o.Customer).SingleAsync(o => o.Id == s.OrderId)).Customer.RelationId;

        Assert.Contains("mode", (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto()))).Errors.Keys);
        Assert.Contains("carrierRelationId", (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Carrier" }))).Errors.Keys);
        Assert.Contains("carrierRelationId", (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Carrier", CarrierRelationId = customerRelation }))).Errors.Keys);
        var bad = ByCarrier(s);
        bad.UnloadingAddressId = s.OtherAddressId;
        bad.TemperatureC = 2.25m;
        bad.UnloadDate = new DateTime(2026, 11, 12);
        bad.LoadingReference = new string('R', 65);
        var refused = await Transport(t => t.SaveAsync(s.OrderId, bad));
        Assert.Equal(new[] { "loadingReference", "temperatureC", "unloadDate", "unloadingAddressId" }, refused.Errors.Keys.OrderBy(k => k));
        Assert.Contains("temperatureC", (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Own", TemperatureC = 31m }))).Errors.Keys);

        // Eigen transport laat een meegestuurde vervoerder weg; een wijziging moet de versie meesturen, een verouderde versie is een conflict.
        var own = await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Own", CarrierRelationId = s.CarrierId }));
        Assert.Equal(("Own", (int?)null, CurrentUser.Default.Actor(100)), (own.Value.Mode, own.Value.CarrierRelationId, (await LoadTransport(s.OrderId)).CreatedBy));
        Assert.Contains("expectedVersion", (await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Own" }))).Errors.Keys);
        Assert.Equal(ServiceResultStatus.Ok, (await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s, own.Value.Version)))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s, own.Value.Version)))).Status);
    }

    [Fact]
    public async Task Saving_transport_without_a_load_time_keeps_the_one_on_the_order_clearing_is_explicit()
    {
        var s = await OrderAsync();
        var load = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc);
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto { LoadAt = load }));
        var own = await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { Mode = "Own" }));
        Assert.Equal(load, own.Value.LoadAt);
        var cleared = await Transport(t => t.SaveAsync(s.OrderId, new SaveSalesOrderTransportDto { ExpectedVersion = own.Value.Version, Mode = "Own", ClearLoadAt = true }));
        Assert.Null(cleared.Value.LoadAt);
    }

    [Fact]
    public async Task Transport_can_be_removed_until_there_is_a_transport_order()
    {
        var s = await OrderAsync();
        var saved = await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s)));
        // Met een verouderde versie niet; met de versie die je zag wel.
        Assert.Equal(ServiceResultStatus.Conflict, (await Transport(t => t.DeleteAsync(s.OrderId, saved.Value.Version + 1))).Status);
        var removed = await Transport(t => t.DeleteAsync(s.OrderId, saved.Value.Version));
        Assert.Equal((ServiceResultStatus.Ok, false), (removed.Status, removed.Value.Exists));
        await Transport(t => t.SaveAsync(s.OrderId, ByCarrier(s)));
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));
        Assert.Equal(ServiceResultStatus.Conflict, (await Transport(t => t.DeleteAsync(s.OrderId))).Status);
        Assert.True((await Transport(t => t.GetAsync(s.OrderId))).Value.HasTransportOrder);
        Assert.Equal("TO-", SalesOrderDocumentRules.TransportOrderNumber("VO-2026-0012")[..3]);
        Assert.Equal("TO-2026-0012", SalesOrderDocumentRules.TransportOrderNumber("VO-2026-0012"));
    }

    [Fact]
    public async Task The_transport_order_settings_default_to_english_and_europallet()
    {
        var settings = await Transport(t => t.GetSettingsAsync());
        Assert.Equal(("en", "Europallet"), (settings.Language, settings.PalletKind));
        Assert.Contains("language", (await Transport(t => t.UpdateSettingsAsync(new TransportOrderSettingsDto { Language = "de" }))).Errors.Keys);
        var updated = await Transport(t => t.UpdateSettingsAsync(new TransportOrderSettingsDto { Language = "NL", PalletKind = " Blokpallet " }));
        Assert.Equal(("nl", "Blokpallet"), (updated.Value.Language, updated.Value.PalletKind));
        Assert.Equal("Europallet", (await Transport(t => t.UpdateSettingsAsync(new TransportOrderSettingsDto { Language = "en", PalletKind = "" }))).Value.PalletKind);
    }

    private async Task<SalesOrderTransport> LoadTransport(int orderId)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderTransports.AsNoTracking().SingleAsync(t => t.SalesOrderId == orderId);
    }
}
