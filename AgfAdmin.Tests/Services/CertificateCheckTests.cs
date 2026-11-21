using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 1, de controle bij inkoop: een product dat niet in het GGN-certificaat van de leverancier staat, of een certificaat dat
// verlopen is of vóór de levering verloopt, vraagt "Klopt dit?" (409) bij de inkooporder (aanmaken en aanpassen) en bij ontvangst. Een
// waarschuwing, geen blokkade: bevestigd slaat het op, met wie bevestigde. Zonder GGN of zonder producten in het GGN: geen vraag.
public class CertificateCheckTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(Today) };
    private readonly PurchaseOrderEditHarness _harness;
    private PurchaseOrderSeed _seed;
    private int _limeId;
    private int _mangoProductId;
    private int _avocadoProductId;

    public CertificateCheckTests() => _harness = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    private static ICurrentUser User(string name) => new CurrentUser($"id-{name}", name, new[] { "Medewerker" }, Permissions.All);

    private async Task SeedAsync(PurchaseOrderSeed seed = null)
    {
        _seed = seed ?? await PurchaseOrderTestData.SeedAsync(_factory);
        await using var db = _factory.CreateContext();
        var lime = new Product { Name = "Limoen" };
        db.Products.Add(lime);
        await db.SaveChangesAsync();
        _limeId = lime.Id;
        _avocadoProductId = (await db.ProductVariants.SingleAsync(v => v.Id == _seed.HassId)).ProductId;
        _mangoProductId = (await db.ProductVariants.SingleAsync(v => v.Id == _seed.KentId)).ProductId;
    }

    // Een GGN-certificaat bij de leverancier, voor deze producten en tot deze datum.
    private async Task GgnAsync(DateOnly? validUntil, params int[] productIds)
    {
        await using var db = _factory.CreateContext();
        var relationId = (await db.Vendors.SingleAsync(v => v.Id == _seed.VendorId)).RelationId;
        var certificate = new RelationCertificate
        {
            RelationId = relationId, TypeCode = CertificateRules.GlobalGapIfa, Number = "4049928415684", NumberKey = "4049928415684", ValidUntil = validUntil,
            ProductsPending = productIds.Length == 0, CreatedBy = "Test", UpdatedBy = "Test",
        };
        foreach (var id in productIds) certificate.Products.Add(new RelationCertificateProduct { ProductId = id });
        db.RelationCertificates.Add(certificate);
        await db.SaveChangesAsync();
    }

    private async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request, string user = "Anja")
    {
        await using var db = _factory.CreateContext();
        return await new SupplyCommitmentService(db, User(user)).CreateAsync(request);
    }

    private static string Code(object body) => (string)body?.GetType().GetProperty("code")?.GetValue(body);
    private static List<CertificateCheckDto> Checks(object body) => (List<CertificateCheckDto>)body.GetType().GetProperty("checks")!.GetValue(body);

    [Fact]
    public async Task An_Avocado_Order_At_A_Supplier_With_A_Ggn_For_Mango_And_Lime_Asks_And_Records_Who_Confirmed()
    {
        await SeedAsync();
        await GgnAsync(Today.AddDays(200), _mangoProductId, _limeId);
        var request = PurchaseOrderTestData.OrderRequest(_seed);

        var asked = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Conflict, asked.Status);
        Assert.Equal(CertificateCheck.Code, Code(asked.ConflictBody));
        var check = Checks(asked.ConflictBody).Single();
        Assert.Equal(_avocadoProductId, check.ProductId);
        Assert.Equal(new[] { CertificateCheck.KindNotCovered }, check.Kinds);
        Assert.Equal("GGN 4049928415684 van Nature's Pride geldt voor limoen en mango, niet voor avocado.", check.Message);
        Assert.Equal(new[] { "Limoen", "Mango" }, check.Certificates.Single().ProductNames);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(0, await db.SupplyCommitments.CountAsync());
        }

        // "Nee, ik pas het aan" en een mango-order: geen vraag meer.
        var mango = PurchaseOrderTestData.OrderRequest(_seed, variantId: _seed.KentId);
        mango.Lines[0].ExpectedNetWeightKg = 884m;
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(mango)).Status);

        // "Ja, klopt": dezelfde opdracht met het bevestigde product slaat op, met de bevestiging.
        request.ConfirmedCertificateProductIds = new() { _avocadoProductId };
        var saved = await CreateAsync(request, "Piet");
        Assert.Equal(ServiceResultStatus.Created, saved.Status);
        await using var verify = _factory.CreateContext();
        var confirmation = await verify.CertificateConfirmations.SingleAsync();
        Assert.Equal((CertificateCheck.ContextPurchaseOrder, saved.Value.Id, _seed.VendorId, _avocadoProductId, "NotCovered", "Piet", (int?)null),
            (confirmation.Context, confirmation.SupplyCommitmentId, confirmation.VendorId, confirmation.ProductId, confirmation.Kinds, confirmation.ConfirmedBy, confirmation.PalletContentLineId));
        Assert.Equal(check.Message, confirmation.Message);
        // Append-only.
        confirmation.Message = "anders";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => verify.SaveChangesAsync());
    }

    [Fact]
    public async Task No_Question_Without_A_Ggn_Or_With_A_Ggn_Without_Products_Or_When_The_Product_Is_Covered()
    {
        await SeedAsync();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PurchaseOrderTestData.OrderRequest(_seed))).Status);
        await GgnAsync(null); // overgenomen oude GGN: "producten nog invullen"
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PurchaseOrderTestData.OrderRequest(_seed))).Status);
        await GgnAsync(Today.AddDays(200), _avocadoProductId);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PurchaseOrderTestData.OrderRequest(_seed))).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.CertificateConfirmations.CountAsync());
    }

    [Fact]
    public async Task An_Expired_Certificate_Or_One_That_Expires_Before_Delivery_Is_In_The_Same_Question()
    {
        await SeedAsync();
        await GgnAsync(Today.AddDays(-1), _avocadoProductId);
        var expired = await CreateAsync(PurchaseOrderTestData.OrderRequest(_seed));
        var check = Checks(expired.ConflictBody).Single();
        Assert.Equal(new[] { CertificateCheck.KindExpired }, check.Kinds);
        Assert.Equal("Het GGN-certificaat is verlopen op 19-10-2026.", check.Message);

        await using (var db = _factory.CreateContext())
        {
            var certificate = await db.RelationCertificates.SingleAsync();
            certificate.ValidUntil = Today.AddDays(5);
            await db.SaveChangesAsync();
        }
        // Verwachte levering op 2 november: het certificaat verloopt op 25 oktober.
        var before = await CreateAsync(PurchaseOrderTestData.OrderRequest(_seed));
        check = Checks(before.ConflictBody).Single();
        Assert.Equal(new[] { CertificateCheck.KindExpiresBeforeDelivery }, check.Kinds);
        Assert.Equal("Het GGN-certificaat verloopt op 25-10-2026, vóór de verwachte levering op 02-11-2026.", check.Message);

        // Een ander product dan in het (verlopen) certificaat: beide in één melding.
        var mango = PurchaseOrderTestData.OrderRequest(_seed, variantId: _seed.KentId);
        await using (var db = _factory.CreateContext())
        {
            var certificate = await db.RelationCertificates.SingleAsync();
            certificate.ValidUntil = Today.AddDays(-3);
            await db.SaveChangesAsync();
        }
        check = Checks((await CreateAsync(mango)).ConflictBody).Single();
        Assert.Equal(new[] { CertificateCheck.KindNotCovered, CertificateCheck.KindExpired }, check.Kinds);
    }

    [Fact]
    public async Task Editing_An_Order_Only_Asks_For_Products_That_Are_New_On_It()
    {
        var order = await _harness.OrderAsync();
        await SeedAsync(_harness.Seed);
        await GgnAsync(Today.AddDays(200), _mangoProductId);
        // De avocado stond er al: een andere hoeveelheid vraagt niets.
        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.Lines[0].ExpectedQuantity = 200m;
        edit.Lines[0].ExpectedNetWeightKg = 800m;
        var saved = await _harness.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);

        // Een nieuwe regel met limoen (niet in het GGN): de vraag, daarna bevestigd.
        await using (var db = _factory.CreateContext())
        {
            db.ProductVariants.Add(new ProductVariant { ProductId = _limeId, Name = "Tahiti", Origin = "Brazilië", Packaging = "4kg Box", DefaultPrice = 5m });
            await db.SaveChangesAsync();
        }
        int limeVariant;
        await using (var db = _factory.CreateContext()) limeVariant = (await db.ProductVariants.SingleAsync(v => v.ProductId == _limeId)).Id;
        var add = PurchaseOrderEditHarness.EditOf(saved.Value);
        add.Lines.Add(new UpdatePurchaseOrderLineDto { ProductVariantId = limeVariant, ExpectedQuantity = 10m, ExpectedUnit = "box", ExpectedNetWeightKg = 40m });
        var asked = await _harness.RunAsync(s => s.UpdateAsync(order.Id, add));
        Assert.Equal(ServiceResultStatus.Conflict, asked.Status);
        Assert.Equal(new[] { _limeId }, Checks(asked.ConflictBody).Select(c => c.ProductId));
        add.ConfirmedCertificateProductIds = new() { _limeId };
        Assert.Equal(ServiceResultStatus.Ok, (await _harness.RunAsync(s => s.UpdateAsync(order.Id, add))).Status);
        await using var verify = _factory.CreateContext();
        Assert.Equal(new[] { (order.Id, _limeId) }, await verify.CertificateConfirmations.Select(c => new ValueTuple<int, int>(c.SupplyCommitmentId, c.ProductId)).ToListAsync());
    }

    [Fact]
    public async Task A_Receipt_Of_A_Product_Outside_The_Ggn_Asks_And_Records_The_Confirmation_With_The_Content_Line()
    {
        await SeedAsync();
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        await GgnAsync(Today.AddDays(200), _mangoProductId);
        int palletId;
        await using (var db = _factory.CreateContext())
        {
            var pallet = await new PalletService(db, User("Anja")).CreateAsync(new CreatePalletDto { RequestId = Guid.NewGuid(), Reference = "PAL-CERT", LogisticsStatus = "Received", LocationContext = "Warehouse", Reason = "Aankomst" });
            palletId = pallet.Value.Id;
        }
        var request = new AddPalletContentLineDto { RequestId = Guid.NewGuid(), SupplyCommitmentLineId = order.Lines[0].Id, LotNumber = "LOT-1", Quantity = 40m, Unit = "box", NetWeightKg = 160m, Reason = "Geteld" };
        async Task<ServiceResult<PalletContentLineDto>> ReceiveAsync()
        {
            await using var db = _factory.CreateContext();
            return await new PalletService(db, User("Jan")).AddContentLineAsync(palletId, request);
        }
        var asked = await ReceiveAsync();
        Assert.Equal(ServiceResultStatus.Conflict, asked.Status);
        Assert.Equal(CertificateCheck.Code, Code(asked.ConflictBody));
        request.ConfirmCertificate = true;
        var received = await ReceiveAsync();
        Assert.Equal(ServiceResultStatus.Created, received.Status);
        await using var verify = _factory.CreateContext();
        var confirmation = await verify.CertificateConfirmations.SingleAsync();
        Assert.Equal((CertificateCheck.ContextReceipt, order.Id, (int?)received.Value.Id, "Jan"), (confirmation.Context, confirmation.SupplyCommitmentId, confirmation.PalletContentLineId, confirmation.ConfirmedBy));
        // Een herhaling van dezelfde ontvangst geeft dezelfde regel terug, zonder tweede bevestiging.
        Assert.Equal(received.Value.Id, (await ReceiveAsync()).Value.Id);
        Assert.Equal(1, await verify.CertificateConfirmations.CountAsync());
    }
}
