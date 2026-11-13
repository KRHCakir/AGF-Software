using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Documenten deel 3: e-mailadressen per relatie (en van de loods), per adres de documentsoorten en Aan of Cc. Een afleveradres kan eigen
// ontvangers hebben; anders gelden die van de relatie. Een verstuurd mailconcept verandert of verdwijnt nooit (regel 32).
public class DocumentRecipientServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<T> RunAsync<T>(Func<DocumentRecipientService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new DocumentRecipientService(db));
    }

    private async Task<(int RelationId, int StoreId, int OtherAddressId)> RelationAsync()
    {
        await using var db = _factory.CreateContext();
        var relation = new Relation { Name = "Groothandel Noord", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        relation.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Delivery, Label = "Winkel Centrum", Line1 = "Markt 1", City = "Groningen", CountryCode = "NL" });
        var other = new Relation { Name = "Iemand anders", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        other.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Delivery, Line1 = "Elders 9", City = "Venlo", CountryCode = "NL" });
        db.AddRange(relation, other);
        await db.SaveChangesAsync();
        return (relation.Id, relation.Addresses.Single().Id, other.Addresses.Single().Id);
    }

    private static DocumentRecipientDto Recipient(string email, string role, int? address, params string[] kinds) => new() { Name = email.Split('@')[0], Email = email, Role = role, Kinds = kinds.ToList(), RelationAddressId = address };

    [Fact]
    public async Task Addresses_per_document_kind_with_to_and_cc_and_a_delivery_address_falls_back_to_the_relation()
    {
        var (relationId, storeId, _) = await RelationAsync();
        var empty = (await RunAsync(s => s.GetForRelationAsync(relationId))).Value;
        Assert.Equal(("0", 0, 1), (empty.Version, empty.Recipients.Count, empty.Addresses.Count));

        var saved = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto
        {
            ExpectedVersion = empty.Version,
            Recipients =
            {
                Recipient(" planning@noord.nl ", "To", null, "LoadingSlip", "TransportOrder"),
                Recipient("inkoop@noord.nl", "Cc", null, "TransportOrder"),
                Recipient("winkel@noord.nl", "To", storeId, "OrderConfirmation"),
            },
        }));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal(new[] { ("planning@noord.nl", "To", "LoadingSlip,TransportOrder", (int?)null), ("inkoop@noord.nl", "Cc", "TransportOrder", null), ("winkel@noord.nl", "To", "OrderConfirmation", storeId) },
            saved.Value.Recipients.Select(r => (r.Email, r.Role, string.Join(",", r.Kinds), r.RelationAddressId)));

        await using var db = _factory.CreateContext();
        var stored = await db.DocumentRecipients.AsNoTracking().Where(r => r.RelationId == relationId).ToListAsync();
        Assert.Equal("planning@noord.nl | inkoop@noord.nl", Lists(DocumentRecipientService.Resolve(stored, null, "TransportOrder")));
        // Het afleveradres heeft een eigen ontvanger voor de orderbevestiging; voor de transportopdracht valt het terug op de relatie.
        Assert.Equal("winkel@noord.nl | ", Lists(DocumentRecipientService.Resolve(stored, storeId, "OrderConfirmation")));
        Assert.Equal("planning@noord.nl | inkoop@noord.nl", Lists(DocumentRecipientService.Resolve(stored, storeId, "TransportOrder")));
        Assert.Equal(" | ", Lists(DocumentRecipientService.Resolve(stored, null, "Invoice")));
    }

    // "aan | cc" als tekst (een tuple van arrays vergelijkt op referentie).
    private static string Lists((List<string> To, List<string> Cc) value) => $"{string.Join(",", value.To)} | {string.Join(",", value.Cc)}";

    [Fact]
    public async Task Invalid_addresses_are_refused_a_typo_is_a_warning_and_a_stale_version_is_a_conflict()
    {
        var (relationId, _, otherAddressId) = await RelationAsync();
        var refused = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto
        {
            ExpectedVersion = "0",
            Recipients = { Recipient("geen-adres", "To", null, "LoadingSlip"), Recipient("a@b.nl", "Bcc", null), Recipient("x@y.nl", "To", otherAddressId, "Other"), Recipient("dubbel@noord.nl", "To", null, "Other"), Recipient("DUBBEL@noord.nl", "Cc", null, "Invoice") },
        }));
        Assert.Equal(new[] { "recipients[0].email", "recipients[1].kinds", "recipients[1].role", "recipients[2].relationAddressId", "recipients[4].email" }, refused.Errors.Keys.OrderBy(k => k));

        var typo = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { Recipient("planning@gmial.com", "To", null, "LoadingSlip") } }));
        Assert.Equal(ServiceResultStatus.Ok, typo.Status);
        Assert.Equal(("recipients[0].email", "planning@gmail.com"), (typo.Value.Warnings.Single().Field, typo.Value.Warnings.Single().Suggestion));

        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { } }))).Status);
        var twice = typo.Value.Recipients.Single();
        Assert.Contains("recipients[1].id", (await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = typo.Value.Version, Recipients = { twice, new DocumentRecipientDto { Id = twice.Id, Email = "ander@noord.nl", Role = "To", Kinds = { "Other" } } } }))).Errors.Keys);
        var cleared = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = typo.Value.Version, Recipients = { } }));
        Assert.Equal(("0", 0), (cleared.Value.Version, cleared.Value.Recipients.Count));
    }

    [Fact]
    public async Task An_unknown_id_is_refused_and_a_removed_delivery_address_makes_an_open_list_stale()
    {
        var (relationId, storeId, _) = await RelationAsync();
        var unknown = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { new DocumentRecipientDto { Id = 999, Email = "a@noord.nl", Role = "To", Kinds = { "Other" } } } }));
        Assert.Equal(new[] { "recipients[0].id" }, unknown.Errors.Keys);

        var saved = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { Recipient("planning@noord.nl", "To", null, "LoadingSlip"), Recipient("winkel@noord.nl", "To", storeId, "Other") } }));
        // Het afleveradres verdwijnt, en zijn ontvanger via cascade: wie de oude lijst nog open heeft, krijgt een 409 (de versie volgt de inhoud).
        await using (var db = _factory.CreateContext())
        {
            db.RelationAddresses.Remove(await db.RelationAddresses.SingleAsync(a => a.Id == storeId));
            await db.SaveChangesAsync();
        }
        var stale = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = saved.Value.Version, Recipients = { saved.Value.Recipients[0] } }));
        Assert.Equal(ServiceResultStatus.Conflict, stale.Status);
    }

    [Fact]
    public async Task Only_delivery_addresses_are_offered_and_accepted()
    {
        int relationId, billingId;
        await using (var db = _factory.CreateContext())
        {
            var relation = new Relation { Name = "Factuur BV", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            relation.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Billing, Line1 = "Postbus 1", City = "Zwolle", CountryCode = "NL" });
            relation.Addresses.Add(new RelationAddress { Purpose = AddressPurpose.Delivery, Line1 = "Dok 2", City = "Zwolle", CountryCode = "NL" });
            db.Add(relation);
            await db.SaveChangesAsync();
            relationId = relation.Id;
            billingId = relation.Addresses.Single(a => a.Purpose == AddressPurpose.Billing).Id;
        }
        Assert.Equal(new[] { AddressPurpose.Delivery }, (await RunAsync(s => s.GetForRelationAsync(relationId))).Value.Addresses.Select(a => a.Purpose));
        var refused = await RunAsync(s => s.SaveForRelationAsync(relationId, new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { Recipient("a@b.nl", "To", billingId, "Other") } }));
        Assert.Equal(new[] { "recipients[0].relationAddressId" }, refused.Errors.Keys);
    }

    [Fact]
    public async Task The_warehouse_has_its_own_addresses_without_a_relation()
    {
        var saved = await RunAsync(s => s.SaveWarehouseAsync(new SaveDocumentRecipientsDto { ExpectedVersion = "0", Recipients = { Recipient("loods@mijnagf.nl", "To", null, "LoadingSlip") } }));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal("loods@mijnagf.nl | ", Lists(await RunAsync(s => s.ResolveAsync(DocumentMailRules.OwnerWarehouse, null, null, "LoadingSlip"))));
        Assert.Empty((await RunAsync(s => s.GetWarehouseAsync())).Addresses);
    }

    [Fact]
    public async Task A_sent_mail_draft_never_changes_or_disappears_and_a_draft_is_never_deleted()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using (var db = _factory.CreateContext()) await new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto());
        int draftId;
        await using (var db = _factory.CreateContext())
        {
            var document = await db.SalesOrderDocuments.SingleAsync(d => d.SalesOrderId == orderId);
            var draft = new MailDraft { SalesOrderId = orderId, DocumentId = document.Id, DocumentKind = document.Kind, Status = DocumentMailRules.StatusDraft, To = "loods@mijnagf.nl", Subject = "Laadbon", Body = "Tekst", CreatedBy = "Admin", UpdatedBy = "Admin" };
            db.MailDrafts.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.Id;
            draft.Body = "Aangepast";
            await db.SaveChangesAsync();
            draft.Status = DocumentMailRules.StatusSent;
            draft.SentAt = DateTime.UtcNow;
            draft.SentBy = "Admin";
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var sent = await db.MailDrafts.SingleAsync(d => d.Id == draftId);
            sent.Body = "Achteraf veranderd";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.MailDrafts.Remove(await db.MailDrafts.SingleAsync(d => d.Id == draftId));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
