using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Aanvoer plannen als inkooporder: ordernummer, bestelgegevens, gelijktijdig aanmaken en idempotente herhaling.
public class SupplyCommitmentOrderTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_AssignsRunningOrderNumber_AndStoresOrderFields()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var first = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.OrderedOn = new DateOnly(2026, 10, 19);
        request.OrderChannel = "Email";
        request.Note = "  Bevestigd door Piet  ";
        var second = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);

        Assert.Equal("IO-2026-0001", first.OrderNumber);
        Assert.Equal("IO-2026-0002", second.OrderNumber);
        Assert.Equal("NP-88123", first.SupplierReference);
        Assert.Equal("Phone", first.OrderChannel);
        // Weggelaten besteldatum = vandaag (Europe/Amsterdam, vaste testkalender).
        Assert.Equal(new DateOnly(2026, 10, 20), first.OrderedOn);
        Assert.Equal(new DateOnly(2026, 10, 19), second.OrderedOn);
        Assert.Equal("Bevestigd door Piet", second.Note);
    }

    [Fact]
    public async Task OrderNumber_ParallelCreation_GetsConsecutiveDistinctNumbers()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));

        // Deterministische race: precies als de buitenste aanmaak opslaat (nummer 0002 al gekozen), legt een andere aanmaak 0002 vast.
        // De buitenste botst op de unieke index en krijgt bij de nieuwe poging 0003; er ontstaat geen gat en geen dubbel nummer.
        SupplyCommitmentDto inner = null;
        var outer = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed), new ConcurrentSaveInterceptor(async () => inner = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed))));

        Assert.Equal("IO-2026-0002", inner.OrderNumber);
        Assert.Equal("IO-2026-0003", outer.OrderNumber);
        await using var verify = _factory.CreateContext();
        Assert.Equal(new[] { 1, 2, 3 }, await verify.SupplyCommitments.OrderBy(c => c.OrderSequence).Select(c => c.OrderSequence).ToListAsync());
    }

    [Fact]
    public async Task OrderNumber_ParallelCreation_ManyRacesInARow_NoGapsNoDuplicates()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var created = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var outer = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed), new ConcurrentSaveInterceptor(async () => created.Add((await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed))).OrderNumber)));
            created.Add(outer.OrderNumber);
        }
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"IO-2026-{n:D4}"), created.OrderBy(n => n));
    }

    [Fact]
    public async Task Create_Replay_ReturnsSameNumber_OldPayloadWithoutOrderFieldsStillReplays()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var requestId = Guid.NewGuid();
        var first = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, requestId));
        var again = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, requestId));
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.OrderNumber, again.OrderNumber);

        // Een bevroren opdracht van vóór deze velden (zonder referentie, kanaal en datum) blijft dezelfde opdracht.
        var oldId = Guid.NewGuid();
        var old = PurchaseOrderTestData.OrderRequest(seed, oldId);
        old.SupplierReference = null;
        old.OrderChannel = null;
        var stored = await PurchaseOrderTestData.CreateOrderAsync(_factory, old);
        _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 22)); // twee dagen later opnieuw verstuurd
        var replayed = await PurchaseOrderTestData.CreateOrderAsync(_factory, old);
        Assert.Equal(stored.OrderNumber, replayed.OrderNumber);

        // Andere inhoud met dezelfde RequestId: conflict.
        var changed = PurchaseOrderTestData.OrderRequest(seed, requestId);
        changed.SupplierReference = "NP-99999";
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.Conflict, (await new SupplyCommitmentService(db).CreateAsync(changed)).Status);
        var otherDate = PurchaseOrderTestData.OrderRequest(seed, requestId);
        otherDate.OrderedOn = new DateOnly(2026, 9, 1);
        Assert.Equal(ServiceResultStatus.Conflict, (await new SupplyCommitmentService(db).CreateAsync(otherDate)).Status);
    }

    [Fact]
    public async Task Create_RejectsUnknownChannel_AndTooLongReference()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.OrderChannel = "Fax";
        request.SupplierReference = new string('x', 101);
        await using var db = _factory.CreateContext();
        var result = await new SupplyCommitmentService(db).CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("orderChannel", result.Errors.Keys);
        Assert.Contains("supplierReference", result.Errors.Keys);
    }
}
