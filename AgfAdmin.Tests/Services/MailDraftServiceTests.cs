using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Documenten deel 3: bij elke nieuwe documentversie staat meteen een mailconcept klaar, met de ontvangers voor die soort (de loods voor de
// laadbon, de vervoerder voor de transportopdracht), onderwerp en tekst uit het sjabloon en de handtekening. Zonder adres een melding.
public class MailDraftServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<(int OrderId, int CarrierId)> OrderAsync(bool carrierAddresses = true, bool warehouseAddresses = true)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using var db = _factory.CreateContext();
        var carrier = new Relation { Name = "Transport Zeeland B.V.", IsCarrier = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Add(carrier);
        db.Add(new CompanySettings { CompanyName = "Kwekerij Voorbeeld B.V." });
        db.Add(new MailSettings { Signature = "Kwekerij Voorbeeld B.V.\nLoodsweg 1, Barendrecht" });
        await db.SaveChangesAsync();
        DocumentRecipient Recipient(string owner, int? relation, string email, string role, int kinds) => new() { OwnerKind = owner, RelationId = relation, Name = email, Email = email, Role = role, Kinds = kinds, CreatedBy = "t", UpdatedBy = "t" };
        if (carrierAddresses) db.AddRange(Recipient("Relation", carrier.Id, "planning@zeeland.nl", "To", 4), Recipient("Relation", carrier.Id, "kantoor@zeeland.nl", "Cc", 4), Recipient("Relation", carrier.Id, "facturen@zeeland.nl", "To", 8));
        if (warehouseAddresses) db.AddRange(Recipient("Warehouse", null, "loods@voorbeeld.nl", "To", 2));
        await db.SaveChangesAsync();
        return (orderId, carrier.Id);
    }

    private async Task<T> RunAsync<T>(Func<AgfAdmin.Data.AppDbContext, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(db);
    }

    private Task<ServiceResult<SalesOrderTransportDto>> CarrierAsync(int orderId, int carrierId, int? version = null, decimal temperature = 2m) =>
        RunAsync(db => new SalesOrderTransportService(db).SaveAsync(orderId, new SaveSalesOrderTransportDto { ExpectedVersion = version, Mode = "Carrier", CarrierRelationId = carrierId, TemperatureC = temperature }));

    private Task<List<MailDraftDto>> DraftsAsync(int orderId) => RunAsync(db => new MailDraftService(db).ForOrderAsync(orderId));

    [Fact]
    public async Task Confirming_makes_a_draft_per_document_with_the_recipients_for_that_kind_and_never_sends()
    {
        var (orderId, carrierId) = await OrderAsync();
        await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto { LoadAt = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc) }));

        var drafts = await DraftsAsync(orderId);
        var slip = drafts.Single(d => d.DocumentKind == "LoadingSlip");
        Assert.Equal(("Draft", "loods@voorbeeld.nl", "", null), (slip.Status, slip.To, slip.Cc, slip.MissingRecipient));
        Assert.StartsWith("Laadbon LB-", slip.Subject);
        Assert.Contains("Te laden op: 13-11-2026 06:30", slip.Body);
        Assert.EndsWith("Te laden op: 13-11-2026 06:30.\n\nKwekerij Voorbeeld B.V.\nLoodsweg 1, Barendrecht", slip.Body);
        Assert.DoesNotContain("groet", slip.Body); // de handtekening is de afsluiting: nooit twee keer een groet
        var transport = drafts.Single(d => d.DocumentKind == "TransportOrder");
        // Alleen de adressen voor de transportopdracht (de facturen-mail niet).
        Assert.Equal(("planning@zeeland.nl", "kantoor@zeeland.nl"), (transport.To, transport.Cc));
        Assert.StartsWith("Transportopdracht TO-", transport.Subject);
        Assert.DoesNotContain("{", transport.Body);
        await using var db = _factory.CreateContext();
        Assert.All(await db.MailDrafts.ToListAsync(), d => Assert.Null(d.SentAt));
    }

    [Fact]
    public async Task The_global_list_adds_missing_drafts_for_older_confirmed_orders_without_bringing_back_discarded_ones()
    {
        var (orderId, carrierId) = await OrderAsync();
        await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto { LoadAt = new DateTime(2026, 11, 13, 5, 30, 0, DateTimeKind.Utc) }));
        // Zoals een order van vóór de conceptfunctie: documenten wel, concepten niet.
        await using (var db = _factory.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM MailDrafts");
        }

        Assert.Equal(2, await RunAsync(db => new MailDraftService(db).OpenCountAsync()));
        var open = await RunAsync(db => new MailDraftService(db).OpenAsync(0, 10));
        Assert.Equal(2, open.Count);

        var slip = open.Single(d => d.DocumentKind == "LoadingSlip");
        await RunAsync(db => new MailDraftService(db).DiscardAsync(slip.Id, new MailDraftActionDto { ExpectedVersion = slip.Version }));
        Assert.Equal(1, await RunAsync(db => new MailDraftService(db).OpenCountAsync()));
    }

    [Fact]
    public async Task Without_an_address_the_draft_is_there_with_a_notice_and_own_transport_gets_none()
    {
        var (orderId, carrierId) = await OrderAsync(carrierAddresses: false, warehouseAddresses: false);
        await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var drafts = await DraftsAsync(orderId);
        Assert.Equal(("de loods", (int?)null), (drafts.Single(d => d.DocumentKind == "LoadingSlip").MissingRecipient, drafts.Single(d => d.DocumentKind == "LoadingSlip").MissingRelationId));
        Assert.Equal(("Transport Zeeland B.V.", (int?)carrierId, ""), (drafts.Single(d => d.DocumentKind == "TransportOrder").MissingRecipient, drafts.Single(d => d.DocumentKind == "TransportOrder").MissingRelationId, drafts.Single(d => d.DocumentKind == "TransportOrder").To));

        // Komt er later een adres bij de loods, dan werkt de lijst Te versturen het open concept bij, zonder dat iemand de order opent.
        await using (var db = _factory.CreateContext())
        {
            db.DocumentRecipients.Add(new DocumentRecipient { OwnerKind = "Warehouse", Name = "Loods", Email = "loods@voorbeeld.nl", Role = "To", Kinds = 2, CreatedBy = "t", UpdatedBy = "t" });
            await db.SaveChangesAsync();
        }
        var listed = (await RunAsync(db => new MailDraftService(db).OpenAsync())).Single(d => d.SalesOrderId == orderId && d.DocumentKind == "LoadingSlip");
        Assert.Equal(("loods@voorbeeld.nl", (string)null), (listed.To, listed.MissingRecipient));

        var (ownOrder, _) = (orderId + 0, 0);
        var transport = await RunAsync(db => new SalesOrderTransportService(db).GetAsync(ownOrder));
        await RunAsync(db => new SalesOrderTransportService(db).SaveAsync(ownOrder, new SaveSalesOrderTransportDto { ExpectedVersion = transport.Value.Version, Mode = "Own", TemperatureC = 3m }));
        // Eigen transport: geen vervoerder om naar te mailen; het concept van de vorige versie (naar de vervoerder) is vervangen.
        Assert.Equal(new[] { "LoadingSlip" }, (await DraftsAsync(orderId)).Select(d => d.DocumentKind));
        Assert.Equal(1, await RunAsync(db => db.MailDrafts.CountAsync(d => d.DocumentKind == "TransportOrder" && d.Status == "Superseded")));
    }

    [Fact]
    public async Task A_draft_of_a_document_that_was_passed_on_can_no_longer_be_changed_or_discarded()
    {
        var (orderId, _) = await OrderAsync();
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var slip = (await DraftsAsync(orderId)).Single(d => d.DocumentKind == "LoadingSlip");
        await RunAsync(db => db.SalesOrderDocuments.Where(d => d.Id == slip.DocumentId).ExecuteUpdateAsync(u => u.SetProperty(d => d.SentAt, DateTime.UtcNow).SetProperty(d => d.SentBy, "Admin")));
        var edit = await RunAsync(db => new MailDraftService(db).UpdateAsync(slip.Id, new UpdateMailDraftDto { ExpectedVersion = slip.Version, To = "loods@voorbeeld.nl", Subject = "s", Body = "b" }));
        var discard = await RunAsync(db => new MailDraftService(db).DiscardAsync(slip.Id, new MailDraftActionDto { ExpectedVersion = slip.Version }));
        Assert.Equal((ServiceResultStatus.Conflict, ServiceResultStatus.Conflict), (edit.Status, discard.Status));
    }

    [Fact]
    public async Task A_new_version_replaces_the_open_draft_and_says_when_the_previous_one_went_out()
    {
        var (orderId, carrierId) = await OrderAsync();
        var saved = await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var first = (await DraftsAsync(orderId)).Single(d => d.DocumentKind == "TransportOrder");
        // Versie 1 is doorgegeven; daarna een kouder transport: versie 2 met een nieuw concept.
        await RunAsync(db => new LoadingSlipService(db).MarkSentAsync(orderId, first.DocumentId));
        var current = await RunAsync(db => new SalesOrderTransportService(db).GetAsync(orderId));
        await CarrierAsync(orderId, carrierId, current.Value.Version, 1.5m);
        var drafts = await DraftsAsync(orderId);
        var second = drafts.Single(d => d.DocumentKind == "TransportOrder");
        Assert.Equal((2, "Draft"), (second.DocumentVersion, second.Status));
        Assert.NotNull(second.PreviousSentAt);
        Assert.Equal("Superseded", (await RunAsync(db => new MailDraftService(db).GetAsync(first.Id))).Value.Status);
        Assert.Null(drafts.Single(d => d.DocumentKind == "LoadingSlip").PreviousSentAt);
    }

    [Fact]
    public async Task A_draft_a_concurrent_request_made_for_an_older_version_is_never_listed_and_is_superseded_next_time()
    {
        var (orderId, carrierId) = await OrderAsync();
        await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var current = await RunAsync(db => new SalesOrderTransportService(db).GetAsync(orderId));
        await CarrierAsync(orderId, carrierId, current.Value.Version, 1.5m);
        // Nagebootst: een tweede aanvraag zette nog een open concept op versie 1 terwijl versie 2 al een concept had.
        await using (var db = _factory.CreateContext())
        {
            var first = await db.MailDrafts.SingleAsync(d => d.DocumentKind == "TransportOrder" && d.Document.Version == 1);
            first.Status = "Draft";
            await db.SaveChangesAsync();
        }
        Assert.Equal(new[] { 2 }, (await RunAsync(db => new MailDraftService(db).OpenAsync())).Where(d => d.DocumentKind == "TransportOrder").Select(d => d.DocumentVersion));
        await RunAsync(db => new MailDraftService(db).EnsureDraftsAsync(orderId));
        Assert.Equal("Superseded", await RunAsync(db => db.MailDrafts.Where(d => d.DocumentKind == "TransportOrder" && d.Document.Version == 1).Select(d => d.Status).SingleAsync()));
    }

    [Fact]
    public async Task A_draft_can_be_edited_or_discarded_while_open_with_valid_addresses_and_the_version_seen()
    {
        var (orderId, carrierId) = await OrderAsync();
        await CarrierAsync(orderId, carrierId);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var draft = (await DraftsAsync(orderId)).Single(d => d.DocumentKind == "LoadingSlip");

        var refused = await RunAsync(db => new MailDraftService(db).UpdateAsync(draft.Id, new UpdateMailDraftDto { ExpectedVersion = draft.Version, To = "loods@voorbeeld.nl, geen-adres", Subject = "", Body = "x" }));
        Assert.Equal(new[] { "subject", "to" }, refused.Errors.Keys.OrderBy(k => k));
        var edited = await RunAsync(db => new MailDraftService(db).UpdateAsync(draft.Id, new UpdateMailDraftDto { ExpectedVersion = draft.Version, To = "loods@voorbeeld.nl; chef@voorbeeld.nl", Cc = "LOODS@voorbeeld.nl", Subject = "Laadbon morgen", Body = "Graag klaarzetten." }));
        Assert.Equal(("loods@voorbeeld.nl, chef@voorbeeld.nl", "Laadbon morgen"), (edited.Value.To, edited.Value.Subject));
        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(db => new MailDraftService(db).UpdateAsync(draft.Id, new UpdateMailDraftDto { ExpectedVersion = draft.Version, To = "a@b.nl", Subject = "s", Body = "b" }))).Status);

        var discarded = await RunAsync(db => new MailDraftService(db).DiscardAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = edited.Value.Version }));
        Assert.Equal("Discarded", discarded.Value.Status);
        Assert.DoesNotContain(await DraftsAsync(orderId), d => d.Id == draft.Id);
        Assert.Equal(1, await RunAsync(db => db.SalesOrderDocuments.CountAsync(d => d.SalesOrderId == orderId && d.Kind == "LoadingSlip")));
        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(db => new MailDraftService(db).DiscardAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = discarded.Value.Version }))).Status);
    }

    [Fact]
    public async Task Without_a_signature_the_text_ends_with_one_standard_closing()
    {
        var (orderId, _) = await OrderAsync();
        await RunAsync(db => db.MailSettings.ExecuteUpdateAsync(u => u.SetProperty(m => m.Signature, (string)null)));
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var slip = (await DraftsAsync(orderId)).Single(d => d.DocumentKind == "LoadingSlip");
        Assert.EndsWith(".\n\nMet vriendelijke groet,\nKwekerij Voorbeeld B.V.", slip.Body);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(slip.Body, "groet"));
    }

    [Fact]
    public async Task An_own_template_is_used_for_new_drafts_and_empty_goes_back_to_the_standard_text()
    {
        var (orderId, carrierId) = await OrderAsync();
        var saved = await RunAsync(db => new MailTemplateService(db).SaveAsync(new MailTemplateDto { Kind = "LoadingSlip", Subject = "{document} {nummer} voor {klant}", Body = "Hallo loods,\n\nOrder {order}, partij {partij}.\n\n{bedrijf}" }));
        Assert.False(saved.Value.Templates.Single(t => t.Kind == "LoadingSlip").IsDefault);
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        var slip = (await DraftsAsync(orderId)).Single(d => d.DocumentKind == "LoadingSlip");
        Assert.StartsWith("Laadbon LB-", slip.Subject);
        Assert.EndsWith("voor Customer T", slip.Subject);
        Assert.StartsWith("Hallo loods,", slip.Body);

        Assert.Contains("subject", (await RunAsync(db => new MailTemplateService(db).SaveAsync(new MailTemplateDto { Kind = "LoadingSlip", Subject = "", Body = "alleen tekst" }))).Errors.Keys);
        var reset = await RunAsync(db => new MailTemplateService(db).SaveAsync(new MailTemplateDto { Kind = "LoadingSlip", Subject = "", Body = "" }));
        Assert.True(reset.Value.Templates.Single(t => t.Kind == "LoadingSlip").IsDefault);
        Assert.Contains("{laaddatum}", reset.Value.Placeholders);
    }
}
