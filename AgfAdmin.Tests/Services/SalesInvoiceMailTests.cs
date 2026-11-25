using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Services.Mail;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1: bij Definitief staat er een mailconcept klaar voor de factuur-ontvangers van de klant (documenten deel 3), met de PDF als
// bijlage. Er wordt niets verstuurd zonder de klik op Versturen; daarna is de factuur verstuurd (één keer).
public sealed class SalesInvoiceMailTests : IDisposable
{
    static SalesInvoiceMailTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly FakeMailSender _fake = new();

    public void Dispose() => _factory.Dispose();

    private async Task<SalesInvoiceDto> FinalAsync(bool withRecipient)
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        if (withRecipient)
        {
            await using var db = _factory.CreateContext();
            var relationId = await db.Customers.Where(c => c.Id == a.Seed.CustomerId).Select(c => c.RelationId).SingleAsync();
            db.Add(new DocumentRecipient { OwnerKind = "Relation", RelationId = relationId, Name = "Administratie", Email = "facturen@klant-t.example", Role = "To",
                Kinds = DocumentMailRules.Flag(DocumentMailRules.KindInvoice), CreatedBy = "t", UpdatedBy = "t" });
            await db.SaveChangesAsync();
        }
        var concept = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Loodskosten", 35m))).Value;
        await using var write = _factory.CreateContext();
        return (await new SalesInvoiceService(write).FinalizeAsync(concept.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = concept.Version })).Value;
    }

    private async Task<List<MailDraftDto>> DraftsAsync(int invoiceId)
    {
        await using var db = _factory.CreateContext();
        return await new MailDraftService(db).ForInvoiceAsync(invoiceId);
    }

    [Fact]
    public async Task Final_Makes_A_Draft_For_The_Invoice_Recipients_Of_The_Customer_And_Sends_Nothing()
    {
        var final = await FinalAsync(withRecipient: true);
        var draft = Assert.Single(await DraftsAsync(final.Id));
        Assert.Equal((DocumentMailRules.KindInvoice, DocumentMailRules.StatusDraft, "facturen@klant-t.example"), (draft.DocumentKind, draft.Status, draft.To));
        Assert.Equal($"Factuur {final.InvoiceNumber} – Customer T", draft.Subject);
        Assert.Contains(final.InvoiceNumber, draft.Body);
        Assert.Contains(final.SalesLotNumber, draft.Body);
        Assert.Equal((null, final.Id, final.InvoiceNumber, 1), (draft.SalesOrderId, draft.InvoiceId.Value, draft.DocumentNumber, draft.DocumentVersion));
        Assert.Empty(_fake.Sent);
        await using var db = _factory.CreateContext();
        Assert.Contains((await new MailDraftService(db).OpenAsync(0, 50)), d => d.Id == draft.Id);
    }

    [Fact]
    public async Task Without_An_Invoice_Recipient_The_Customer_Is_Named_As_Missing()
    {
        var final = await FinalAsync(withRecipient: false);
        var draft = Assert.Single(await DraftsAsync(final.Id));
        Assert.Equal(("Customer T", ""), (draft.MissingRecipient, draft.To ?? ""));
    }

    [Fact]
    public async Task Sending_Attaches_The_Invoice_Pdf_And_Records_That_The_Invoice_Was_Sent()
    {
        var final = await FinalAsync(withRecipient: true);
        var draft = Assert.Single(await DraftsAsync(final.Id));
        await using (var db = _factory.CreateContext())
        {
            var protection = new EphemeralDataProtectionProvider();
            var result = await new MailSendService(db, _fake, new MailSettingsService(db, _fake, protection), NullLogger<MailSendService>.Instance)
                .SendAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = draft.Version });
            Assert.Equal(ServiceResultStatus.Ok, result.Status);
        }
        var mail = Assert.Single(_fake.Sent);
        Assert.Equal($"Factuur {final.InvoiceNumber}.pdf", mail.AttachmentName);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(mail.Attachment, 0, 4));
        await using var check = _factory.CreateContext();
        var document = await check.SalesInvoiceDocuments.SingleAsync();
        Assert.NotNull(document.SentAt);
        Assert.Equal(DocumentMailRules.StatusSent, (await check.MailDrafts.SingleAsync()).Status);
    }
}
