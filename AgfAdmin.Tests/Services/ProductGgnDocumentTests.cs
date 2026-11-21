using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 2: het GGN van het product op de laadbon en de transportopdracht. Per pallet of partij het GGN van de ontvangst;
// één verkoopregel uit meer partijen geeft op de transportopdracht alle verschillende nummers; leeg is leeg (geen streepje). Aan/uit per
// documentsoort; het eigen CoC-nummer in de koptekst. Een bestaande versie blijft zoals ze was; nog steeds nergens een prijs.
public class ProductGgnDocumentTests : IDisposable
{
    static ProductGgnDocumentTests() => QuestPDF.Settings.License = LicenseType.Community;

    private const string GrowerA = "4049928415684";
    private const string GrowerB = "8712345678906";
    private const string OwnCoc = "4063061999992";
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private sealed record Setup(SalesSeed Seed, int OrderId, int BoxLine, int ColliLine, int CarrierId);

    // Een verkooporder: 50 dozen uit twee partijen (30 van P-WH met GGN A, 20 van P-WH2 met GGN B) en 10 colli (zonder GGN), met vervoerder.
    private async Task<Setup> OrderAsync()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, colli) = await SalesTestData.SeedOrderAsync(_factory, seed);
        int carrierId;
        await using (var db = _factory.CreateContext())
        {
            // Het GGN zoals bij ontvangst vastgelegd (rechtstreeks: een inhoudsregel verandert in de software nooit meer).
            await db.Database.ExecuteSqlRawAsync("UPDATE \"PalletContentLines\" SET \"Ggn\" = {0} WHERE \"Id\" = {1}", GrowerA, seed.ApprovedBox);
            await db.Database.ExecuteSqlRawAsync("UPDATE \"PalletContentLines\" SET \"Ggn\" = {0} WHERE \"Id\" = {1}", GrowerB, seed.SecondPalletApprovedBox);
            var carrier = new Relation { Name = "Transport Zeeland B.V.", IsCarrier = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Add(carrier);
            var euro = new CostArticle { RequestId = Guid.NewGuid(), Name = "Euro-pallet", NameKey = "euro-pallet", Kind = "Packaging", DefaultUnitPrice = 9.50m, VatPercentage = 21m, CreatedBy = "test", CreatedAt = DateTime.UtcNow };
            db.Add(new SalesOrderPackagingLine { SalesOrderId = orderId, CostArticle = euro, Quantity = 2, UnitPrice = 9.50m, VatPercentage = 21m });
            await db.SaveChangesAsync();
            carrierId = carrier.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var reservations = new StockReservationService(db);
            Assert.Equal(ServiceResultStatus.Created, (await reservations.ReserveAsync(orderId, box, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 30m, Unit = "box", NetWeightKg = 120m })).Status);
            Assert.Equal(ServiceResultStatus.Created, (await reservations.ReserveAsync(orderId, box, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = seed.SecondPalletApprovedBox, Quantity = 20m, Unit = "box", NetWeightKg = 80m })).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new SalesOrderTransportService(db).SaveAsync(orderId, new SaveSalesOrderTransportDto { Mode = "Carrier", CarrierRelationId = carrierId })).Status);
        }
        return new Setup(seed, orderId, box, colli, carrierId);
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

    private async Task OwnCocAsync(string number, DateOnly? validUntil = null, bool active = true, string type = CertificateRules.GlobalGapCoc)
    {
        await using var db = _factory.CreateContext();
        db.RelationCertificates.Add(new RelationCertificate { RelationId = null, TypeCode = type, Number = number, NumberKey = number, ValidUntil = validUntil, IsActive = active, CreatedBy = "Test", UpdatedBy = "Test" });
        await db.SaveChangesAsync();
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task The_loading_slip_shows_the_ggn_per_pallet_and_the_transport_order_all_different_numbers_of_a_line()
    {
        var s = await OrderAsync();
        await OwnCocAsync("4063061000005", new DateOnly(2020, 1, 1));  // verlopen: telt niet
        await OwnCocAsync(OwnCoc);
        await OwnCocAsync("SKAL-12345", type: "Skal");                  // andere soort: telt niet
        var confirmed = await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));
        Assert.Equal(ServiceResultStatus.Ok, confirmed.Status);

        var slipVersion = Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip));
        var slip = LoadingSlipBuilder.Deserialize(slipVersion.SnapshotJson);
        Assert.Equal(OwnCoc, slip.OwnCocNumber);
        Assert.Equal(new[] { (30m, "P-WH", GrowerA), (20m, "P-WH2", GrowerB), (10m, (string)null, (string)null) }, slip.Rows.Select(r => (r.Quantity, r.PalletReference, r.Ggn)));
        var transportVersion = Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder));
        var transport = TransportOrderBuilder.Deserialize(transportVersion.SnapshotJson);
        Assert.Equal(OwnCoc, transport.OwnCocNumber);
        Assert.Equal(new[] { (50m, $"{GrowerA}, {GrowerB}"), (10m, (string)null) }, transport.Rows.Select(r => (r.Quantity, r.Ggn)));

        // In de PDF: onder de omschrijving, en de CoC in de koptekst. Een regel zonder GGN heeft geen tekst (geen streepje of n.v.t.).
        var slipText = PdfText(LoadingSlipPdf.Render(slipVersion, slip));
        Assert.Contains($"GGN {GrowerA}", slipText);
        Assert.Contains($"GGN {GrowerB}", slipText);
        Assert.Contains($"GLOBALG.A.P. CoC {OwnCoc}", slipText);
        Assert.DoesNotContain("n.v.t.", slipText);
        Assert.Equal(2, slipText.Split("GGN ").Length - 1);
        var transportText = PdfText(TransportOrderPdf.Render(transportVersion, transport));
        Assert.Contains($"GGN {GrowerA}, {GrowerB}", transportText);
        Assert.Contains($"GLOBALG.A.P. CoC {OwnCoc}", transportText);
        // Nog steeds nergens een prijs, bedrag of btw.
        foreach (var text in new[] { slipText, transportText, slipVersion.SnapshotJson, transportVersion.SnapshotJson })
        {
            foreach (var word in new[] { "€", "EUR", "Price", "Prijs", "Amount", "Bedrag", "VAT", "btw", "9.5", "9,50" }) Assert.DoesNotContain(word, text);
        }
    }

    [Fact]
    public async Task Turning_the_ggn_off_per_document_kind_gives_a_new_version_without_it_and_the_old_version_stays_as_it_was()
    {
        var s = await OrderAsync();
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));
        var first = Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip));

        // Standaard aan; uit bij de laadbon (de transportopdracht houdt het).
        var settings = await Slips(x => x.GetSettingsAsync());
        Assert.True(settings.ShowGgn);
        settings.ShowGgn = false;
        Assert.Equal(ServiceResultStatus.Ok, (await Slips(x => x.UpdateSettingsAsync(settings))).Status);
        Assert.True(await Slips(x => x.RefreshAsync(s.OrderId)));
        var slips = await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip);
        Assert.Equal(2, slips.Count);
        Assert.All(LoadingSlipBuilder.Deserialize(slips[1].SnapshotJson).Rows, r => Assert.Null(r.Ggn));
        Assert.Equal(first.SnapshotJson, slips[0].SnapshotJson);
        Assert.Equal(GrowerA, LoadingSlipBuilder.Deserialize(slips[0].SnapshotJson).Rows[0].Ggn);
        Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder));

        // Een oudere client zonder het veld laat de instelling staan.
        Assert.Equal(ServiceResultStatus.Ok, (await Slips(x => x.UpdateSettingsAsync(new LoadingSlipSettingsDto { Language = "en", ShowCustomer = true, ShowSupplier = true }))).Status);
        Assert.False((await Slips(x => x.GetSettingsAsync())).ShowGgn);

        await using (var db = _factory.CreateContext())
        {
            var transport = new SalesOrderTransportService(db);
            var current = await transport.GetSettingsAsync();
            Assert.True(current.ShowGgn);
            current.ShowGgn = false;
            Assert.Equal(ServiceResultStatus.Ok, (await transport.UpdateSettingsAsync(current)).Status);
        }
        Assert.True(await Slips(x => x.RefreshAsync(s.OrderId)));
        var transports = await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindTransportOrder);
        Assert.Equal(2, transports.Count);
        Assert.All(TransportOrderBuilder.Deserialize(transports[1].SnapshotJson).Rows, r => Assert.Null(r.Ggn));
        Assert.Equal($"{GrowerA}, {GrowerB}", TransportOrderBuilder.Deserialize(transports[0].SnapshotJson).Rows[0].Ggn);
    }

    [Fact]
    public async Task A_later_change_of_the_own_coc_gives_a_new_version_and_the_existing_one_keeps_its_snapshot()
    {
        var s = await OrderAsync();
        await Slips(x => x.ConfirmAsync(s.OrderId, new ConfirmSalesOrderDto()));
        Assert.Null(LoadingSlipBuilder.Deserialize(Assert.Single(await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip)).SnapshotJson).OwnCocNumber);
        Assert.False(await Slips(x => x.RefreshAsync(s.OrderId)), "dezelfde inhoud geeft nooit een nieuwe versie");
        await OwnCocAsync(OwnCoc);
        Assert.True(await Slips(x => x.RefreshAsync(s.OrderId)));
        var slips = await VersionsAsync(s.OrderId, SalesOrderDocumentRules.KindLoadingSlip);
        Assert.Equal((null, OwnCoc), (LoadingSlipBuilder.Deserialize(slips[0].SnapshotJson).OwnCocNumber, LoadingSlipBuilder.Deserialize(slips[1].SnapshotJson).OwnCocNumber));
        // Een PDF van versie 1 wordt altijd uit haar eigen snapshot gemaakt: zonder CoC.
        Assert.DoesNotContain("CoC", PdfText(LoadingSlipPdf.Render(slips[0], LoadingSlipBuilder.Deserialize(slips[0].SnapshotJson))));
    }

    [Fact]
    public void The_ggn_of_a_reservation_is_that_of_the_receipt_and_for_direct_trade_that_of_the_purchase_line()
    {
        var line = new SupplyCommitmentLine { Ggn = GrowerA };
        Assert.Equal(GrowerA, LoadingSlipBuilder.ProductGgn(new StockReservation { SupplyCommitmentLine = line }));
        Assert.Equal(GrowerB, LoadingSlipBuilder.ProductGgn(new StockReservation { SupplyCommitmentLine = line, PalletContentLine = new PalletContentLine { Ggn = GrowerB, SupplyCommitmentLine = line } }));
        // Bij ontvangst bewust leeggemaakt: dan ook leeg op het document (niet terug naar de inkoopregel).
        Assert.Null(LoadingSlipBuilder.ProductGgn(new StockReservation { SupplyCommitmentLine = line, PalletContentLine = new PalletContentLine { Ggn = null, SupplyCommitmentLine = line } }));
        // Een bon zonder GGN en zonder CoC heeft dezelfde vingerafdruk als een bon van vóór deze ronde (lege velden tellen niet mee).
        var row = new LoadingSlipRow(1m, "box", null, "Avocado", "Hass", null, null, null, null, null, null, null);
        var json = System.Text.Json.JsonSerializer.Serialize(row, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        Assert.DoesNotContain("ggn", json, StringComparison.OrdinalIgnoreCase);
    }
}
