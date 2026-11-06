using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Kostenartikelen (type Dienst): de standaardsoorten staan er na de migratie al; de gebruiker kan er zelf bij maken (dubbelcontrole op
    // de naam zonder hoofdletter- of spatieverschil) en een kostenartikel inactief maken. Nooit verwijderen: factuurregels verwijzen ernaar.
    public sealed class CostArticleService
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        // Zonder ingelogde gebruiker (tests, seed) de standaardgebruiker; in de API altijd de gebruiker van het verzoek.
        public CostArticleService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _db = db;
            _user = currentUser ?? CurrentUser.Default;
        }

        // Actieve eerst in vaste volgorde (standaardsoorten, dan eigen op naam); met includeInactive ook de inactieve.
        public async Task<IReadOnlyList<CostArticleDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var query = _db.CostArticles.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }
            return await query.OrderByDescending(c => c.IsActive).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CostArticleDto
                {
                    Id = c.Id, Name = c.Name, IsStandard = c.IsStandard, IsActive = c.IsActive, SortOrder = c.SortOrder,
                    LineCount = _db.IncomingInvoiceLines.Count(l => l.CostArticleId == c.Id)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ServiceResult<CostArticleDto>> CreateAsync(CreateCostArticleDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            var name = CostArticleRules.NormalizeName(request?.Name);
            if (name == null)
            {
                errors["name"] = new[] { "A name is required." };
            }
            else if (name.Length > CostArticleRules.MaxNameLength)
            {
                errors["name"] = new[] { $"This field may contain at most {CostArticleRules.MaxNameLength} characters." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<CostArticleDto>.Validation(errors);
            }

            var replay = await _db.CostArticles.AsNoTracking().SingleOrDefaultAsync(c => c.RequestId == request.RequestId, cancellationToken);
            if (replay != null)
            {
                return replay.NameKey == CostArticleRules.NameKey(name)
                    ? ServiceResult<CostArticleDto>.Ok(await DtoAsync(replay.Id, cancellationToken))
                    : ServiceResult<CostArticleDto>.Conflict($"RequestId {request.RequestId} was already used for cost article {replay.Name}.");
            }
            var key = CostArticleRules.NameKey(name);
            var existing = await _db.CostArticles.AsNoTracking().SingleOrDefaultAsync(c => c.NameKey == key, cancellationToken);
            if (existing != null)
            {
                return ServiceResult<CostArticleDto>.Conflict($"Cost article {existing.Name} already exists{(existing.IsActive ? "" : " (inactive; make it active again)")}.");
            }
            var sortOrder = (await _db.CostArticles.MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? 0) + 10;
            var article = new CostArticle
            {
                RequestId = request.RequestId, Name = name, NameKey = key, IsActive = true, SortOrder = sortOrder,
                CreatedAt = DateTime.UtcNow, CreatedBy = _user.Actor(CostArticleRules.MaxActorLength)
            };
            _db.CostArticles.Add(article);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                // Gelijktijdig dezelfde naam of dezelfde aanvraag: opnieuw beoordelen met wat er nu staat.
                _db.ChangeTracker.Clear();
                return await CreateAsync(request, cancellationToken);
            }
            return ServiceResult<CostArticleDto>.Created(await DtoAsync(article.Id, cancellationToken));
        }

        // Inactief: niet meer te kiezen op een nieuwe factuurregel; bestaande regels en partijkosten blijven zoals ze zijn.
        public async Task<ServiceResult<CostArticleDto>> SetActiveAsync(int id, SetCostArticleActiveDto request, CancellationToken cancellationToken = default)
        {
            if (request?.IsActive == null)
            {
                return ServiceResult<CostArticleDto>.Validation(new Dictionary<string, string[]> { ["isActive"] = new[] { "Say whether the cost article is active." } });
            }
            var article = await _db.CostArticles.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (article == null)
            {
                return ServiceResult<CostArticleDto>.NotFound($"Cost article {id} does not exist.");
            }
            article.IsActive = request.IsActive.Value;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<CostArticleDto>.Ok(await DtoAsync(id, cancellationToken));
        }

        private async Task<CostArticleDto> DtoAsync(int id, CancellationToken cancellationToken) =>
            (await ListAsync(includeInactive: true, cancellationToken)).Single(c => c.Id == id);
    }
}
