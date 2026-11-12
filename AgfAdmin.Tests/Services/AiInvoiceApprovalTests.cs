using System.Text.Json.Nodes;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Services;

// Koppelen en goedkeuren (stap 7a): de leverancier op IBAN, KvK, btw-nummer of naam, de inkooporder op het ordernummer van de leverancier,
// een dubbele factuur en een kostenartikel per regel, alleen als voorstel. Goedkeuren gaat via het gewone opslagpad: "goedgekeurd door" de
// ingelogde gebruiker met model en versie, aangepaste waarden (oud en nieuw), het document als bijlage met hash en de orderkoppeling.
public class AiInvoiceApprovalTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly string _root = Directory.CreateTempSubdirectory("agf-ai-approve-").FullName;
    private readonly FakeDocumentAi _ai = new();
    private readonly DocumentAiOptions _options = DefaultOptions();
    private readonly CurrentUser _user = new("id-steve", "Steve", new[] { "Medewerker" }, Permissions.All);

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
    }

    private AiInvoiceReadingService Reading(AppDbContext db) => new(db, _ai, new InvoiceReader(_ai, Options.Create(_options), NullLogger<InvoiceReader>.Instance),
        new FileDocumentStore(_root), new UploadRateLimiter(TimeProvider.System), Options.Create(_options), _user, TimeProvider.System);

    private async Task<int> VendorAsync(string name, string vat = null, string kvk = null, string iban = null)
    {
        await using var db = _factory.CreateContext();
        var relation = await new RelationService(db, new AlwaysMailServer(), _user).CreateAsync(new SaveRelationDto
        {
            RequestId = Guid.NewGuid(), Name = name, VatNumber = vat, RegistrationNumber = kvk, RegistrationCountry = kvk == null ? null : "NL",
            BankAccounts = iban == null ? new() : new() { new RelationBankAccountDto { Iban = iban, AccountHolder = name } },
            Vendor = new SaveVendorRoleDto { PaymentTermsDays = 30, PaymentTermsEndOfMonth = false },
        });
        Assert.True(relation.Status == ServiceResultStatus.Created, string.Join("; ", relation.Errors?.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}")) ?? Array.Empty<string>()));
        return relation.Value.Vendor.VendorId;
    }

    private async Task<int> OrderAsync(int vendorId, string supplierReference, string status = null)
    {
        int variantId;
        await using (var db = _factory.CreateContext())
        {
            var variant = await db.ProductVariants.FirstOrDefaultAsync();
            if (variant == null)
            {
                variant = new AgfAdmin.Models.ProductVariant { Product = new AgfAdmin.Models.Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box" };
                db.ProductVariants.Add(variant);
                await db.SaveChangesAsync();
            }
            variantId = variant.Id;
        }
        var request = PurchaseOrderTestData.OrderRequest(new PurchaseOrderSeed(vendorId, vendorId, variantId, variantId));
        request.SupplierReference = supplierReference;
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        if (status != null)
        {
            await using var db = _factory.CreateContext();
            (await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id)).Status = status;
            await db.SaveChangesAsync();
        }
        return order.Id;
    }

    private async Task<AiExtractionDto> ProposeAsync(Action<JsonObject> change = null)
    {
        await using var db = _factory.CreateContext();
        var reading = Reading(db);
        await reading.UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true });
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(change));
        var result = await reading.ReadAsync("factuur.pdf", Pdf);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    private async Task<AiMatchesDto> MatchAsync(AiExtractionDto extraction)
    {
        await using var db = _factory.CreateContext();
        return await new AiInvoiceMatching(db).MatchAsync(extraction.Proposal);
    }

    // De invoer zoals het scherm hem stuurt als de gebruiker het voorstel ongewijzigd goedkeurt.
    private static CreatePurchaseInvoiceDto Approve(AiExtractionDto extraction, int vendorId, int? orderId = null, Action<CreatePurchaseInvoiceDto> change = null)
    {
        var p = extraction.Proposal;
        var request = new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = vendorId, InvoiceNumber = p.InvoiceNumber, InvoiceDate = p.InvoiceDate!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Currency = p.Currency, AiExtractionId = extraction.Id, LinkPurchaseOrderId = orderId,
            Lines = p.Lines.Select(l => new CreatePurchaseInvoiceLineDto { Description = l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, VatPercentage = l.VatPercentage }).ToList(),
        };
        change?.Invoke(request);
        return request;
    }

    private async Task<ServiceResult<PurchaseInvoiceDto>> SaveAsync(CreatePurchaseInvoiceDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db, _user).CreateAsync(request);
    }

    [Fact]
    public async Task The_supplier_is_found_on_IBAN_KvK_or_VAT_before_the_name_and_only_suggested_when_unambiguous()
    {
        var byIban = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        await VendorAsync("Fruitimport Zuid Holding");
        var extraction = await ProposeAsync();
        var matches = await MatchAsync(extraction);
        Assert.Equal(byIban, matches.SuggestedVendorId);
        Assert.Equal(new[] { "iban" }, matches.VendorCandidates.First().MatchedOn);

        var byName = await ProposeAsync(p => { p["leverancier_iban"] = ""; p["leverancier_kvk"] = ""; p["leverancier_btw"] = ""; });
        var named = await MatchAsync(byName);
        Assert.Equal(new[] { "Fruitimport Zuid Holding" }, named.VendorCandidates.Select(c => c.Name));
        Assert.NotNull(named.SuggestedVendorId);

        await VendorAsync("Fruitimport Zuid B.V.", kvk: "12345678");
        var both = await MatchAsync(byName);
        Assert.Null(both.SuggestedVendorId);
        Assert.Equal(2, both.VendorCandidates.Count);
        Assert.Contains(both.VendorCandidates, c => c.Name == "Fruitimport Zuid B.V." && c.MatchedOn.SequenceEqual(new[] { "name" }));

        var kvk = await MatchAsync(await ProposeAsync(p => { p["leverancier_iban"] = ""; p["leverancier_btw"] = ""; }));
        Assert.Equal("Fruitimport Zuid B.V.", kvk.VendorCandidates.First(c => c.VendorId == kvk.SuggestedVendorId).Name);
    }

    [Fact]
    public async Task The_order_is_found_on_the_supplier_order_number_and_a_known_invoice_number_is_a_duplicate()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var order = await OrderAsync(vendor, "45 21");
        await OrderAsync(vendor, "4521", status: "Cancelled");
        var other = await VendorAsync("Ander BV");
        await OrderAsync(other, "45-21");
        await CostArticleTestData.SeedStandardAsync(_factory);

        var extraction = await ProposeAsync();
        var matches = await MatchAsync(extraction);
        Assert.Equal(order, matches.SuggestedOrderId);
        Assert.Equal("45 21", Assert.Single(matches.OrderCandidates).SupplierReference);
        Assert.Null(matches.DuplicateInvoice);
        Assert.Equal(new[] { (1, "Transport") }, matches.LineCostArticles.Select(c => (c.Index, c.Name)));

        // Het voorgestelde kostenartikel niet overnemen is geen aanpassing.
        var saved = await SaveAsync(Approve(extraction, vendor, order));
        Assert.Equal((ServiceResultStatus.Created, 0), (saved.Status, saved.Value.AiReading.AdjustedFields));
        var again = await MatchAsync(await ProposeAsync());
        Assert.Equal("F-2026-118", again.DuplicateInvoice.InvoiceNumber);
    }

    [Fact]
    public async Task Approving_saves_through_the_normal_path_records_who_model_and_version_attaches_the_document_and_links_the_order()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var order = await OrderAsync(vendor, "45-21");
        var extraction = await ProposeAsync();
        var saved = await SaveAsync(Approve(extraction, vendor, order));
        Assert.Equal(ServiceResultStatus.Created, saved.Status);
        var invoice = saved.Value;
        Assert.Equal(("Steve", "Claude Haiku 5.5", "claude-haiku-5-5-20261101", 0), (invoice.AiReading.ApprovedBy, invoice.AiReading.ModelName, invoice.AiReading.ModelVersion, invoice.AiReading.AdjustedFields));
        var attachment = Assert.Single(invoice.Attachments);
        Assert.Equal(("factuur.pdf", "application/pdf", UploadedDocuments.Sha256(Pdf)), (attachment.FileName, attachment.MediaType, attachment.Sha256));

        await using var db = _factory.CreateContext();
        var reading = await db.AiExtractions.AsNoTracking().SingleAsync();
        Assert.Equal((AiReadingRules.StatusApproved, "Steve", invoice.Id, "[]"), (reading.Status, reading.ApprovedBy, reading.IncomingInvoiceId, reading.AdjustmentsJson));
        Assert.NotNull(reading.ApprovedAt);
        var reference = await db.SupplyCommitmentInvoiceRefs.AsNoTracking().SingleAsync();
        Assert.Equal((order, "F-2026-118", invoice.Id, 160.00m, "Steve"), (reference.SupplyCommitmentId, reference.InvoiceNumber, reference.IncomingInvoiceId, reference.AmountExclVat, reference.ActorReference));
        Assert.Equal(attachment.Id, (await new PurchaseInvoiceService(db).AttachmentAsync(invoice.Id, attachment.Id)).Id);
        Assert.Null(await new PurchaseInvoiceService(db).AttachmentAsync(invoice.Id + 1, attachment.Id));
    }

    [Fact]
    public async Task Approving_with_an_order_whose_invoice_number_was_already_received_adds_no_second_reference()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var order = await OrderAsync(vendor, "45-21");
        await using (var db = _factory.CreateContext())
        {
            var received = await new PurchaseOrderService(db, _user).AddInvoiceRefAsync(order, new AddPurchaseOrderInvoiceRefDto { RequestId = Guid.NewGuid(), Kind = "Invoice", InvoiceNumber = "f-2026-118" });
            Assert.True(received.Status is ServiceResultStatus.Created or ServiceResultStatus.Ok);
        }
        var extraction = await ProposeAsync();
        var saved = await SaveAsync(Approve(extraction, vendor, order));
        Assert.Equal(ServiceResultStatus.Created, saved.Status);

        await using var check = _factory.CreateContext();
        var reference = await check.SupplyCommitmentInvoiceRefs.AsNoTracking().SingleAsync();
        Assert.Equal(order, reference.SupplyCommitmentId);
        Assert.Equal(AiReadingRules.StatusApproved, (await check.AiExtractions.AsNoTracking().SingleAsync()).Status);
        Assert.Single(check.IncomingInvoices);
    }

    [Fact]
    public async Task What_the_user_changed_before_approving_is_recorded_as_proposal_and_final_value()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var extraction = await ProposeAsync();
        var saved = await SaveAsync(Approve(extraction, vendor, change: r => { r.InvoiceNumber = "F-2026-0118"; r.Lines[0].UnitPrice = 12.40m; }));
        Assert.Equal(2, saved.Value.AiReading.AdjustedFields);
        await using var db = _factory.CreateContext();
        var adjustments = JsonNode.Parse((await db.AiExtractions.AsNoTracking().SingleAsync()).AdjustmentsJson)!.AsArray();
        Assert.Equal(new[] { ("invoiceNumber", "F-2026-118", "F-2026-0118"), ("lines[0].unitPrice", "12.5", "12.4") },
            adjustments.Select(a => (a!["field"]!.GetValue<string>(), a["proposed"]!.GetValue<string>(), a["final"]!.GetValue<string>())));
    }

    [Fact]
    public async Task A_closed_proposal_a_duplicate_invoice_or_an_order_of_another_supplier_saves_nothing()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var other = await VendorAsync("Ander BV");
        var otherOrder = await OrderAsync(other, "45-21");
        var rejected = await ProposeAsync();
        await using (var db = _factory.CreateContext()) await Reading(db).CloseAsync(rejected.Id, new CloseAiExtractionDto { Outcome = "Rejected" });
        var closed = await SaveAsync(Approve(rejected, vendor));
        Assert.Equal(ServiceResultStatus.Validation, closed.Status);
        Assert.True(closed.Errors.ContainsKey("aiExtractionId"));

        var extraction = await ProposeAsync();
        Assert.True((await SaveAsync(Approve(extraction, vendor, otherOrder))).Errors.ContainsKey("linkPurchaseOrderId"));
        Assert.Equal(ServiceResultStatus.Created, (await SaveAsync(Approve(extraction, vendor, change: r => r.AiExtractionId = null))).Status);
        var duplicate = await SaveAsync(Approve(extraction, vendor));
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);

        await using var check = _factory.CreateContext();
        Assert.Equal(AiReadingRules.StatusProposed, (await check.AiExtractions.AsNoTracking().SingleAsync(e => e.Id == extraction.Id)).Status);
        Assert.Empty(check.DocumentAttachments);
        Assert.Single(check.IncomingInvoices);
    }

    [Fact]
    public async Task An_attachment_and_an_approved_reading_never_change_and_no_reading_is_removed()
    {
        var vendor = await VendorAsync("Zuid Import", iban: "NL91ABNA0417164300");
        var extraction = await ProposeAsync();
        await SaveAsync(Approve(extraction, vendor));
        await using var db = _factory.CreateContext();
        (await db.DocumentAttachments.SingleAsync()).FileName = "anders.pdf";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        (await db.AiExtractions.SingleAsync()).AdjustmentsJson = "[{\"field\":\"invoiceNumber\"}]";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AiExtractions.Remove(await db.AiExtractions.SingleAsync());
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Choosing_a_cost_article_is_recorded_as_adjustment_even_when_it_equals_the_suggestion()
    {
        var proposal = new AiInvoiceProposalDto { InvoiceNumber = "F-1", Lines = new() { new AiInvoiceLineProposalDto { Description = "Transport", Quantity = 1, UnitPrice = 10, VatPercentage = 21 } } };
        var matches = new AiMatchesDto { SuggestedVendorId = 5, LineCostArticles = new() { new AiLineCostArticleDto { Index = 0, CostArticleId = 7, Name = "Transport" } } };
        CreatePurchaseInvoiceDto Request(int? costArticleId) => new()
        {
            VendorId = 5, InvoiceNumber = "F-1",
            Lines = new() { new CreatePurchaseInvoiceLineDto { Description = "Transport", Quantity = 1, UnitPrice = 10, VatPercentage = 21, CostArticleId = costArticleId } },
        };

        var chosen = AiInvoiceApproval.Adjustments(proposal, matches, Request(7), "Zuid");
        Assert.Contains(chosen, a => a.Field == "lines[0].costArticleId" && a.Proposed == null && a.Final == "7");
        var none = AiInvoiceApproval.Adjustments(proposal, matches, Request(null), "Zuid");
        Assert.DoesNotContain(none, a => a.Field == "lines[0].costArticleId");
    }

    private sealed class AlwaysMailServer : IEmailDomainChecker
    {
        public Task<EmailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken = default) => Task.FromResult(EmailDomainStatus.HasMailServer);
    }
}
