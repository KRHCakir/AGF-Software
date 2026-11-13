using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // De sjablonen van het mailconcept per documentsoort (documenten deel 3), per bedrijf aan te passen onder Beheer: platte tekst met
    // invulvelden, geen opmaakeditor. Leeg maken = terug naar het vaste standaardsjabloon. Geldt voor nieuwe concepten.
    public sealed class MailTemplateService(AppDbContext db, ICurrentUser user = null)
    {
        private static readonly string[] Kinds = { SalesOrderDocumentRules.KindLoadingSlip, SalesOrderDocumentRules.KindTransportOrder };
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;

        public async Task<MailTemplatesDto> GetAsync(CancellationToken cancellationToken = default)
        {
            var stored = await db.MailTemplates.AsNoTracking().ToListAsync(cancellationToken);
            return new MailTemplatesDto
            {
                Placeholders = MailTemplateRules.Placeholders.ToList(),
                Templates = Kinds.Select(kind => stored.SingleOrDefault(t => t.Kind == kind) is { } own
                    ? new MailTemplateDto { Kind = kind, Subject = own.Subject, Body = own.Body, IsDefault = false }
                    : new MailTemplateDto { Kind = kind, Subject = MailTemplateRules.Default(kind).Subject, Body = MailTemplateRules.Default(kind).Body, IsDefault = true }).ToList(),
            };
        }

        public async Task<ServiceResult<MailTemplatesDto>> SaveAsync(MailTemplateDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var kind = Kinds.FirstOrDefault(k => string.Equals(k, request?.Kind, StringComparison.OrdinalIgnoreCase));
            if (kind == null) ValidationErrors.Add(errors, "kind", $"Kind must be one of: {string.Join(", ", Kinds)}.");
            var subject = (request?.Subject ?? "").Trim();
            var body = (request?.Body ?? "").Trim();
            var reset = subject.Length == 0 && body.Length == 0;
            if (!reset && subject.Length == 0) ValidationErrors.Add(errors, "subject", "Write a subject, or leave both empty for the standard text.");
            if (!reset && body.Length == 0) ValidationErrors.Add(errors, "body", "Write a text, or leave both empty for the standard text.");
            if (subject.Length > DocumentMailRules.MaxSubjectLength) ValidationErrors.Add(errors, "subject", $"At most {DocumentMailRules.MaxSubjectLength} characters.");
            if (body.Length > DocumentMailRules.MaxBodyLength) ValidationErrors.Add(errors, "body", $"At most {DocumentMailRules.MaxBodyLength} characters.");
            if (errors.Count > 0) return ServiceResult<MailTemplatesDto>.Validation(ValidationErrors.ToArrays(errors));
            var stored = await db.MailTemplates.SingleOrDefaultAsync(t => t.Kind == kind, cancellationToken);
            if (reset)
            {
                if (stored != null) db.MailTemplates.Remove(stored);
            }
            else
            {
                if (stored == null) db.MailTemplates.Add(stored = new MailTemplate { Kind = kind });
                stored.Subject = subject;
                stored.Body = body;
                stored.UpdatedAt = DateTime.UtcNow;
                stored.UpdatedBy = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            }
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<MailTemplatesDto>.Ok(await GetAsync(cancellationToken));
        }
    }
}
