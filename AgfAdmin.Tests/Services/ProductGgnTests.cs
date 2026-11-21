using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 2, het GGN van het product op de inkoopregel: een voorstel uit de geldige GGN-certificaten van de leverancier (één
// treffer, meer treffers, geen treffer), altijd te overschrijven of leeg te maken, 13 cijfers. Aanpassen bij een Open order met auditregel,
// bij een definitieve order alleen met een correctie met reden (guardregel 34). Bij ontvangst gaat het mee naar de pallet (zie de laadbon).
public class ProductGgnTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private const string GrowerA = "4049928415684";
    private const string GrowerB = "8712345678906";
    private const string Trader = "4063061000005";
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(Today) };
    private readonly PurchaseOrderEditHarness _h;
    private int _avocado;
    private int _mango;
    private int _lime;

    public ProductGgnTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    private async Task<PurchaseOrderSeed> SeedAsync()
    {
        await _h.OrderAsync(); // zaait de leveranciers en artikelen (en een eerste order zonder GGN)
        await using var db = _factory.CreateContext();
        var lime = new Product { Name = "Limoen" };
        db.Products.Add(lime);
        await db.SaveChangesAsync();
        _lime = lime.Id;
        _avocado = (await db.ProductVariants.SingleAsync(v => v.Id == _h.Seed.HassId)).ProductId;
        _mango = (await db.ProductVariants.SingleAsync(v => v.Id == _h.Seed.KentId)).ProductId;
        return _h.Seed;
    }

    private async Task GgnAsync(string number, DateOnly? validUntil, bool active, params int[] productIds)
    {
        await using var db = _factory.CreateContext();
        var relationId = (await db.Vendors.SingleAsync(v => v.Id == _h.Seed.VendorId)).RelationId;
        var certificate = new RelationCertificate
        {
            RelationId = relationId, TypeCode = CertificateRules.GlobalGapIfa, Number = number, NumberKey = number, ValidUntil = validUntil, IsActive = active,
            ProducerName = $"Teler {number[^3..]}", CreatedBy = "Test", UpdatedBy = "Test",
        };
        foreach (var id in productIds) certificate.Products.Add(new RelationCertificateProduct { ProductId = id });
        db.RelationCertificates.Add(certificate);
        await db.SaveChangesAsync();
    }

    private async Task<ProductGgnProposalsDto> ProposalsAsync(DateOnly? date = null, int? vendorId = null, params int[] products)
    {
        await using var db = _factory.CreateContext();
        var result = await new PurchaseOrderService(db).GgnProposalsAsync(vendorId ?? _h.Seed.VendorId, products, date);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task The_Proposal_Is_One_Ggn_To_Fill_In_A_Choice_Between_Several_Or_Nothing()
    {
        await SeedAsync();
        await GgnAsync(GrowerA, Today.AddDays(200), true, _avocado);
        await GgnAsync(GrowerB, null, true, _avocado, _mango);
        await GgnAsync("4000000000006", Today.AddDays(-1), true, _lime);    // verlopen
        await GgnAsync("4000000000013", Today.AddDays(200), false, _lime);  // gearchiveerd
        await GgnAsync("4000000000020", Today.AddDays(200), true);          // producten nog invullen

        var proposals = await ProposalsAsync(null, null, _avocado, _mango, _lime);
        Assert.Equal(Today, proposals.Date);
        var avocado = proposals.Proposals.Single(p => p.ProductId == _avocado);
        Assert.Equal(ProductGgnRules.ProposalMany, avocado.Kind);
        Assert.Equal(new[] { GrowerA, GrowerB }, avocado.Options.Select(o => o.Ggn));
        var mango = proposals.Proposals.Single(p => p.ProductId == _mango);
        Assert.Equal((ProductGgnRules.ProposalOne, GrowerB, "Teler 906"), (mango.Kind, mango.Options.Single().Ggn, mango.Options.Single().ProducerName));
        Assert.Equal(ProductGgnRules.ProposalNone, proposals.Proposals.Single(p => p.ProductId == _lime).Kind);

        // Op de verwachte leverdatum: een certificaat dat dan verlopen is, telt niet mee.
        var later = await ProposalsAsync(Today.AddDays(201), null, _avocado);
        Assert.Equal((ProductGgnRules.ProposalOne, GrowerB), (later.Proposals.Single().Kind, later.Proposals.Single().Options.Single().Ggn));

        // Een leverancier zonder GGN: geen voorstel; een onbekende leverancier: niet gevonden.
        Assert.Equal(ProductGgnRules.ProposalNone, (await ProposalsAsync(null, _h.Seed.OtherVendorId, _avocado)).Proposals.Single().Kind);
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new PurchaseOrderService(db).GgnProposalsAsync(999_999, new[] { _avocado }, null)).Status);
    }

    [Fact]
    public async Task The_Ggn_Is_Stored_As_Given_Even_When_It_Is_Not_In_The_Suppliers_Certificate()
    {
        var seed = await SeedAsync();
        await GgnAsync(GrowerA, Today.AddDays(200), true, _avocado, _mango);
        // Een handelaar factureert het GGN van zijn teler: dat nummer staat niet in zijn eigen certificaat en wordt toch opgeslagen.
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Lines[0].Ggn = "406 3061 000005";
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        Assert.Equal(Trader, order.Lines.Single().Ggn);
        Assert.Equal(Trader, (await _h.DetailAsync(order.Id)).Lines.Single().Ggn);

        // Leeg = geen GGN; geen 13 cijfers = een melding bij het veld (geen opslag).
        var empty = PurchaseOrderTestData.OrderRequest(seed);
        empty.Lines[0].Ggn = "  ";
        Assert.Null((await PurchaseOrderTestData.CreateOrderAsync(_factory, empty)).Lines.Single().Ggn);
        var wrong = PurchaseOrderTestData.OrderRequest(seed);
        wrong.Lines[0].Ggn = "40499284";
        await using var db = _factory.CreateContext();
        var refused = await new SupplyCommitmentService(db).CreateAsync(wrong);
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("lines[0].ggn", refused.Errors.Keys);
        // Hetzelfde verzoek nog eens (dezelfde requestId) met een ander GGN is een andere opdracht, niet dezelfde.
        var replay = PurchaseOrderTestData.OrderRequest(seed, requestId: request.RequestId);
        replay.Lines[0].Ggn = GrowerA;
        Assert.NotEqual(ServiceResultStatus.Ok, (await new SupplyCommitmentService(db).CreateAsync(replay)).Status);
    }

    [Fact]
    public async Task An_Open_Order_Changes_Its_Ggn_With_An_Audit_Line_Also_After_A_Receipt()
    {
        var seed = await SeedAsync();
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Lines[0].Ggn = GrowerA;
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        await PurchaseOrderTestData.ReceiveAsync(_factory, created.Id, 10m, 40m);

        var edit = PurchaseOrderEditHarness.EditOf(await _h.DetailAsync(created.Id));
        edit.Lines[0].Ggn = GrowerB;
        var changed = await _h.RunAsync(s => s.UpdateAsync(created.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, changed.Status);
        Assert.Equal(GrowerB, changed.Value.Lines[0].Ggn);
        var field = changed.Value.Changes[^1].Fields.Single();
        Assert.Equal(("ggn", GrowerA, GrowerB), (field.Field, field.Old, field.New));

        // Leegmaken mag ook; zonder wijziging komt er geen auditregel.
        var clear = PurchaseOrderEditHarness.EditOf(changed.Value);
        clear.Lines[0].Ggn = "";
        var cleared = await _h.RunAsync(s => s.UpdateAsync(created.Id, clear));
        Assert.Null(cleared.Value.Lines[0].Ggn);
        var same = await _h.RunAsync(s => s.UpdateAsync(created.Id, PurchaseOrderEditHarness.EditOf(cleared.Value)));
        Assert.Equal(cleared.Value.Changes.Count, same.Value.Changes.Count);
        var invalid = PurchaseOrderEditHarness.EditOf(cleared.Value);
        invalid.Lines[0].Ggn = "123";
        Assert.Contains("lines[0].ggn", (await _h.RunAsync(s => s.UpdateAsync(created.Id, invalid))).Errors.Keys);
    }

    [Fact]
    public async Task A_Definitive_Order_Changes_Its_Ggn_Only_With_A_Correction_With_A_Reason()
    {
        var seed = await SeedAsync();
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var finalized = await _h.RunAsync(s => s.FinalizeAsync(created.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0 }));
        var edit = PurchaseOrderEditHarness.EditOf(finalized.Value);
        edit.Lines[0].Ggn = GrowerA;
        Assert.Equal(PurchaseOrderService.DefinitiveCode, PurchaseOrderEditHarness.CodeOf(await _h.RunAsync(s => s.UpdateAsync(created.Id, edit))));

        CorrectPurchaseOrderDto Correction(string ggn, int version) => new()
        {
            RequestId = Guid.NewGuid(), ExpectedVersion = version, Reason = "GGN van de teler volgens de factuur", Lines = { new CorrectPurchaseOrderLineDto { Id = created.Lines[0].Id, Ggn = ggn } },
        };
        Assert.Contains("lines[0].ggn", (await _h.RunAsync(s => s.CorrectAsync(created.Id, Correction("12", finalized.Value.EditVersion)))).Errors.Keys);
        var corrected = await _h.RunAsync(s => s.CorrectAsync(created.Id, Correction(GrowerA, finalized.Value.EditVersion)));
        Assert.Equal(ServiceResultStatus.Ok, corrected.Status);
        Assert.Equal(GrowerA, corrected.Value.Lines[0].Ggn);
        var change = corrected.Value.Changes[^1];
        Assert.Equal(("Correction", "GGN van de teler volgens de factuur", "ggn", (string)null, GrowerA), (change.Kind, change.Reason, change.Fields.Single().Field, change.Fields.Single().Old, change.Fields.Single().New));
        // Een lege tekst maakt het GGN leeg; weggelaten laat het staan.
        var cleared = await _h.RunAsync(s => s.CorrectAsync(created.Id, Correction("", corrected.Value.EditVersion)));
        Assert.Null(cleared.Value.Lines[0].Ggn);
        var untouched = Correction(null, cleared.Value.EditVersion);
        untouched.Note = "Alleen de notitie";
        Assert.Null((await _h.RunAsync(s => s.CorrectAsync(created.Id, untouched))).Value.Lines[0].Ggn);
    }

    [Fact]
    public async Task The_Guard_Refuses_A_Ggn_Change_Without_The_Matching_Audit_Line()
    {
        var seed = await SeedAsync();
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await using (var db = _factory.CreateContext())
        {
            var line = await db.SupplyCommitmentLines.SingleAsync(l => l.SupplyCommitmentId == created.Id);
            line.Ggn = GrowerA;
            var error = await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("edit", error.Message);
        }
        await _h.RunAsync(s => s.FinalizeAsync(created.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0 }));
        await using (var db = _factory.CreateContext())
        {
            // Definitief: een Edit-auditregel volstaat niet, alleen een correctie.
            var line = await db.SupplyCommitmentLines.SingleAsync(l => l.SupplyCommitmentId == created.Id);
            line.Ggn = GrowerA;
            db.PurchaseOrderChanges.Add(new PurchaseOrderChange { RequestId = Guid.NewGuid(), SupplyCommitmentId = created.Id, Kind = PurchaseOrderDocumentRules.KindEdit, ActorReference = "Test", ChangesJson = "[]" });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
