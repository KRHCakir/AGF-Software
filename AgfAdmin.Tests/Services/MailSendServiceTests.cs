using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.Mail;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgfAdmin.Tests.Services;

// Documenten deel 3: versturen gebeurt alleen na een klik (MailSendService), via één dunne laag. Alleen de nagebootste afzender of een SMTP-
// afzender zonder instellingen: nooit het netwerk. Vastgelegd wordt wanneer, door wie, naar wie en met welk bericht-id.
public class MailSendServiceTests : IDisposable
{
    static MailSendServiceTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly FakeMailSender _fake = new();
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly ListLogger _log = new();
    public void Dispose() => _factory.Dispose();

    private sealed class ListLogger : ILogger<MailSendService>
    {
        public List<string> Lines { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private async Task<T> RunAsync<T>(Func<AgfAdmin.Data.AppDbContext, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(db);
    }

    private Task<ServiceResult<MailDraftDto>> SendAsync(int id, int version, bool confirm = false, IMailSender sender = null) => RunAsync(db =>
        new MailSendService(db, sender ?? _fake, new MailSettingsService(db, sender ?? _fake, _protection), _log).SendAsync(id, new MailDraftActionDto { ExpectedVersion = version, ConfirmManyRecipients = confirm }));

    // Een bevestigde order met een vervoerder (adressen voor de transportopdracht) en de loods (adres voor de laadbon).
    private async Task<int> ConfirmedOrderAsync(int carrierTo = 1)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        int carrierId;
        await using (var db = _factory.CreateContext())
        {
            var carrier = new Relation { Name = "Transport Zeeland B.V.", IsCarrier = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Add(carrier);
            await db.SaveChangesAsync();
            carrierId = carrier.Id;
            for (var i = 0; i < carrierTo; i++) db.Add(new DocumentRecipient { OwnerKind = "Relation", RelationId = carrier.Id, Name = "Planning", Email = $"planning{i}@zeeland.nl", Role = "To", Kinds = 4, CreatedBy = "t", UpdatedBy = "t" });
            db.Add(new DocumentRecipient { OwnerKind = "Warehouse", Name = "Loods", Email = "loods@voorbeeld.nl", Role = "To", Kinds = 2, CreatedBy = "t", UpdatedBy = "t" });
            await db.SaveChangesAsync();
        }
        await RunAsync(db => new SalesOrderTransportService(db).SaveAsync(orderId, new SaveSalesOrderTransportDto { Mode = "Carrier", CarrierRelationId = carrierId }));
        await RunAsync(db => new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto()));
        return orderId;
    }

    private async Task<MailDraftDto> DraftAsync(int orderId, string kind) => (await RunAsync(db => new MailDraftService(db).ForOrderAsync(orderId))).Single(d => d.DocumentKind == kind);

    [Fact]
    public async Task Nothing_is_sent_without_the_click_and_a_send_records_who_when_to_whom_and_the_message_id()
    {
        var orderId = await ConfirmedOrderAsync();
        await RunAsync(db => new LoadingSlipService(db).DocumentsAsync(orderId));
        var draft = await DraftAsync(orderId, "TransportOrder");
        Assert.Empty(_fake.Sent);

        var sent = await SendAsync(draft.Id, draft.Version);
        Assert.Equal(("Sent", CurrentUser.Default.Actor(100), "planning0@zeeland.nl", 1), (sent.Value.Status, sent.Value.SentBy, sent.Value.To, sent.Value.Attempts));
        Assert.NotNull(sent.Value.SentAt);
        Assert.EndsWith("@nagebootst.agf.local>", sent.Value.MessageId);
        var mail = Assert.Single(_fake.Sent);
        Assert.Equal(("planning0@zeeland.nl", draft.Subject), (string.Join(",", mail.To), mail.Subject));
        Assert.StartsWith("Transportopdracht TO-", mail.AttachmentName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(mail.Attachment, 0, 4));

        // Het document is verstuurd, met naar wie; nog een keer versturen kan niet.
        var documents = (await RunAsync(db => new LoadingSlipService(db).DocumentsAsync(orderId))).Value;
        var document = documents.Documents.Single(d => d.Kind == "TransportOrder");
        Assert.Equal((CurrentUser.Default.Actor(100), "planning0@zeeland.nl"), (document.SentBy, document.SentTo));
        Assert.Equal(ServiceResultStatus.Conflict, (await SendAsync(draft.Id, sent.Value.Version)).Status);
        Assert.Single(_fake.Sent);
    }

    [Fact]
    public async Task Without_an_address_or_a_sender_nothing_goes_out_and_more_than_ten_recipients_need_a_confirmation()
    {
        var orderId = await ConfirmedOrderAsync(carrierTo: 11);
        var slip = await DraftAsync(orderId, "LoadingSlip");
        // Zonder SMTP-instellingen: niet ingesteld (de SMTP-afzender wordt niet eens aangeroepen, dus geen netwerk).
        var notConfigured = await SendAsync(slip.Id, slip.Version, sender: new SmtpMailSender());
        Assert.Equal((ServiceResultStatus.Conflict, "notConfigured"), (notConfigured.Status, Code(notConfigured)));

        var transport = await DraftAsync(orderId, "TransportOrder");
        var many = await SendAsync(transport.Id, transport.Version);
        Assert.Equal("confirmRecipients", Code(many));
        Assert.Empty(_fake.Sent);
        Assert.Equal("Sent", (await SendAsync(transport.Id, transport.Version, confirm: true)).Value.Status);

        var emptied = await RunAsync(db => new MailDraftService(db).UpdateAsync(slip.Id, new UpdateMailDraftDto { ExpectedVersion = slip.Version, To = "", Subject = slip.Subject, Body = slip.Body }));
        Assert.Equal("noRecipient", Code(await SendAsync(slip.Id, emptied.Value.Version)));
    }

    private static string Code(ServiceResult<MailDraftDto> result) => (string)result.ConflictBody?.GetType().GetProperty("code")?.GetValue(result.ConflictBody);

    [Fact]
    public async Task A_failed_send_stays_with_the_reason_without_the_password_and_can_be_sent_again()
    {
        var orderId = await ConfirmedOrderAsync();
        var saved = await RunAsync(db => new MailSettingsService(db, _fake, _protection).SaveAsync(new MailSettingsDto { Host = "smtp.voorbeeld.nl", Port = 587, Security = "StartTls", UserName = "verkoop", Password = "Geheim-123!", FromAddress = "verkoop@voorbeeld.nl" }));
        Assert.Equal((true, (string)null, true), (saved.Value.HasPassword, saved.Value.Password, saved.Value.Configured));
        await using (var db = _factory.CreateContext())
        {
            var stored = await db.MailSettings.AsNoTracking().SingleAsync();
            Assert.DoesNotContain("Geheim-123!", stored.ProtectedPassword);
        }
        var draft = await DraftAsync(orderId, "LoadingSlip");
        _fake.FailWith = "AuthenticationException: 535 login verkoop / Geheim-123! refused";
        var failed = await SendAsync(draft.Id, draft.Version);
        Assert.Equal(("Failed", 1), (failed.Value.Status, failed.Value.Attempts));
        Assert.Equal("AuthenticationException: 535 login verkoop / *** refused", failed.Value.LastError);
        Assert.All(_log.Lines, line => Assert.DoesNotContain("Geheim-123!", line));
        Assert.Contains(_log.Lines, line => line.Contains("niet verstuurd"));

        var again = await SendAsync(draft.Id, failed.Value.Version);
        Assert.Equal(("Sent", 2, (string)null), (again.Value.Status, again.Value.Attempts, again.Value.LastError));
        // Wachtwoord ongewijzigd laten en daarna wissen.
        var kept = await RunAsync(db => new MailSettingsService(db, _fake, _protection).SaveAsync(new MailSettingsDto { Host = "smtp.voorbeeld.nl", Port = 465, Security = "SslOnConnect", UserName = "verkoop", FromAddress = "verkoop@voorbeeld.nl" }));
        Assert.True(kept.Value.HasPassword);
        Assert.False((await RunAsync(db => new MailSettingsService(db, _fake, _protection).SaveAsync(new MailSettingsDto { Host = "smtp.voorbeeld.nl", Port = 465, FromAddress = "verkoop@voorbeeld.nl", ClearPassword = true }))).Value.HasPassword);
    }

    [Fact]
    public async Task An_older_version_of_a_document_is_never_sent()
    {
        var orderId = await ConfirmedOrderAsync();
        var first = await DraftAsync(orderId, "LoadingSlip");
        await RunAsync(db => new LoadingSlipService(db).UpdateLoadingAsync(orderId, new UpdateSalesOrderLoadingDto { LicensePlate = "BX-44-ZZ" }));
        // Normaal is het oude concept dan vervangen (en dus niet meer te versturen); nagebootst: het staat door een race nog open.
        int version;
        await using (var db = _factory.CreateContext())
        {
            var stale = await db.MailDrafts.SingleAsync(d => d.Id == first.Id);
            Assert.Equal("Superseded", stale.Status);
            stale.Status = "Draft";
            await db.SaveChangesAsync();
            version = stale.Version;
        }
        Assert.Equal("superseded", Code(await SendAsync(first.Id, version)));
        Assert.Empty(_fake.Sent);
    }

    [Fact]
    public async Task At_most_ten_send_attempts_per_minute_per_user_even_on_the_same_draft()
    {
        var orderId = await ConfirmedOrderAsync();
        var limiter = new MailSendLimiter();
        var draft = await DraftAsync(orderId, "LoadingSlip");
        var version = draft.Version;
        // Tien mislukte pogingen op hetzelfde concept tellen als tien; de elfde wacht.
        for (var i = 0; i < DocumentMailRules.MaxSendsPerMinute; i++)
        {
            _fake.FailWith = "SmtpCommandException: 550 refused";
            var failed = await RunAsync(db => new MailSendService(db, _fake, new MailSettingsService(db, _fake, _protection), _log, null, limiter).SendAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = version }));
            Assert.Equal("Failed", failed.Value.Status);
            version = failed.Value.Version;
        }
        var blocked = await RunAsync(db => new MailSendService(db, _fake, new MailSettingsService(db, _fake, _protection), _log, null, limiter).SendAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = version }));
        Assert.Equal("rateLimited", Code(blocked));
        Assert.Empty(_fake.Sent);
    }

    [Fact]
    public async Task An_uncertain_outcome_stays_sending_and_needs_a_confirmation_before_sending_again()
    {
        var orderId = await ConfirmedOrderAsync();
        var draft = await DraftAsync(orderId, "LoadingSlip");
        var sender = new UncertainSender();
        var first = await SendAsync(draft.Id, draft.Version, sender: sender);
        Assert.Equal("Sending", first.Value.Status);
        Assert.Contains("onbekend", first.Value.LastError);

        Assert.Equal("uncertain", Code(await SendAsync(draft.Id, first.Value.Version, sender: sender)));
        var confirmed = await RunAsync(db => new MailSendService(db, _fake, new MailSettingsService(db, _fake, _protection), _log).SendAsync(draft.Id, new MailDraftActionDto { ExpectedVersion = first.Value.Version, ConfirmResend = true }));
        Assert.Equal(("Sent", (string)null), (confirmed.Value.Status, confirmed.Value.LastError));
    }

    private sealed class UncertainSender : IMailSender
    {
        public string Kind => FakeMailSender.Name;
        public Task<MailSendResult> SendAsync(OutgoingMail mail, SmtpOptions smtp, CancellationToken cancellationToken) =>
            Task.FromResult(new MailSendResult(false, null, "IOException: connection lost", true));
    }
}
