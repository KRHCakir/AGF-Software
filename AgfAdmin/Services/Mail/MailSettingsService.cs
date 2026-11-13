using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.Mail
{
    // De SMTP-afzender onder Beheer (documenten deel 3). Het wachtwoord wordt versleuteld opgeslagen met ASP.NET Data Protection, komt nooit
    // terug in een antwoord en nooit in een log. Ook niet in de repo: alles staat in de database van het bedrijf.
    public sealed class MailSettingsService(AppDbContext db, IMailSender sender, IDataProtectionProvider protection, ICurrentUser user = null)
    {
        private const string Purpose = "AgfAdmin.MailSettings.Password.v1";
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        private IDataProtector Protector => protection.CreateProtector(Purpose);

        public async Task<MailSettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            var settings = await db.MailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken) ?? new MailSettings();
            return new MailSettingsDto
            {
                Host = settings.Host, Port = settings.Port, Security = settings.Security, UserName = settings.UserName, HasPassword = settings.ProtectedPassword != null,
                FromAddress = settings.FromAddress, FromName = settings.FromName, Signature = settings.Signature, Sender = sender.Kind, Configured = Configured(settings),
            };
        }

        public async Task<MailStatusDto> StatusAsync(CancellationToken cancellationToken = default)
        {
            var settings = await db.MailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            return new MailStatusDto { Configured = Configured(settings), Sender = sender.Kind };
        }

        public async Task<ServiceResult<MailSettingsDto>> SaveAsync(MailSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null) return ServiceResult<MailSettingsDto>.Validation(new Dictionary<string, string[]> { ["request"] = new[] { "A request body is required." } });
            var host = ValidationErrors.OptionalText(request.Host, 255, "host", errors);
            if (request.Port is < 1 or > 65535) ValidationErrors.Add(errors, "port", "Port from 1 to 65535.");
            var security = DocumentMailRules.Securities.FirstOrDefault(s => string.Equals(s, request.Security, StringComparison.OrdinalIgnoreCase)) ?? DocumentMailRules.SecurityStartTls;
            var userName = ValidationErrors.OptionalText(request.UserName, 255, "userName", errors);
            var from = ValidationErrors.OptionalText(request.FromAddress, EmailRules.MaxLength, "fromAddress", errors);
            if (from != null && !EmailRules.IsStrict(from)) ValidationErrors.Add(errors, "fromAddress", EmailRules.InvalidMessage);
            if (host != null && from == null) ValidationErrors.Add(errors, "fromAddress", "Fill in the address mail is sent from.");
            var fromName = ValidationErrors.OptionalText(request.FromName, DocumentMailRules.MaxNameLength, "fromName", errors);
            var signature = ValidationErrors.OptionalText(request.Signature, 2000, "signature", errors);
            if (errors.Count > 0) return ServiceResult<MailSettingsDto>.Validation(ValidationErrors.ToArrays(errors));
            var settings = await db.MailSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null) db.MailSettings.Add(settings = new MailSettings());
            settings.Host = host;
            settings.Port = request.Port;
            settings.Security = security;
            settings.UserName = userName;
            if (request.ClearPassword) settings.ProtectedPassword = null;
            else if (!string.IsNullOrEmpty(request.Password)) settings.ProtectedPassword = Protector.Protect(request.Password);
            settings.FromAddress = from;
            settings.FromName = fromName;
            settings.Signature = signature;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedBy = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<MailSettingsDto>.Ok(await GetAsync(cancellationToken));
        }

        // Wat de afzender nodig heeft, met het ontsleutelde wachtwoord; alleen voor het versturen zelf.
        internal SmtpOptions SmtpOf(MailSettings settings) =>
            new(settings?.Host, settings?.Port ?? 587, settings?.Security, settings?.UserName, settings?.ProtectedPassword == null ? null : Protector.Unprotect(settings.ProtectedPassword));

        internal bool Configured(MailSettings settings) =>
            sender.Kind == FakeMailSender.Name || (!string.IsNullOrWhiteSpace(settings?.Host) && !string.IsNullOrWhiteSpace(settings?.FromAddress));
    }
}
