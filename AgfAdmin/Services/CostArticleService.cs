using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Kostenartikelen (type Dienst): de standaardsoorten staan er na de migratie al; de gebruiker kan er zelf bij maken (dubbelcontrole op
    // de naam zonder hoofdletter- of spatieverschil) en een kostenartikel inactief maken. Nooit verwijderen: factuurregels verwijzen ernaar.
    // Emballage (soort Packaging: pallets, kratten) staat in dezelfde tabel, met een standaardprijs en btw; de naam is uniek over beide soorten.
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

        // Actieve eerst in vaste volgorde (standaardsoorten, dan eigen op naam); met includeInactive ook de inactieve; met kind één soort.
        public async Task<IReadOnlyList<CostArticleDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default, string kind = null)
        {
            var query = _db.CostArticles.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }
            if (kind != null)
            {
                query = query.Where(c => c.Kind == kind);
            }
            return await query.OrderByDescending(c => c.IsActive).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CostArticleDto
                {
                    Id = c.Id, Name = c.Name, Kind = c.Kind, DefaultUnitPrice = c.DefaultUnitPrice, VatPercentage = c.VatPercentage,
                    IsStandard = c.IsStandard, IsActive = c.IsActive, SortOrder = c.SortOrder,
                    LineCount = _db.IncomingInvoiceLines.Count(l => l.CostArticleId == c.Id) + _db.SalesOrderPackagingLines.Count(l => l.CostArticleId == c.Id)
                })
                .ToListAsync(cancellationToken);
        }

        public Task<ServiceResult<CostArticleDto>> CreateAsync(CreateCostArticleDto request, CancellationToken cancellationToken = default) =>
            CreateAsync(request?.RequestId, request?.Name, CostArticleRules.KindCost, null, null, new Dictionary<string, string[]>(), cancellationToken);

        // Emballage-artikel: naam, optionele standaardprijs per stuk en een verplicht btw-percentage.
        public Task<ServiceResult<CostArticleDto>> CreatePackagingAsync(SavePackagingArticleDto request, CancellationToken cancellationToken = default) =>
            CreateAsync(request?.RequestId, request?.Name, CostArticleRules.KindPackaging, request?.DefaultUnitPrice, request?.VatPercentage,
                PriceErrors(request), cancellationToken);

        // Prijs en btw van een emballage-artikel aanpassen; geldt voor nieuwe verkoopregels, opgeslagen regels houden hun eigen prijs en btw.
        public async Task<ServiceResult<CostArticleDto>> UpdatePackagingAsync(int id, SavePackagingArticleDto request, CancellationToken cancellationToken = default)
        {
            var errors = PriceErrors(request);
            if (errors.Count > 0)
            {
                return ServiceResult<CostArticleDto>.Validation(errors);
            }
            var article = await _db.CostArticles.SingleOrDefaultAsync(c => c.Id == id && c.Kind == CostArticleRules.KindPackaging, cancellationToken);
            if (article == null)
            {
                return ServiceResult<CostArticleDto>.NotFound($"Packaging article {id} does not exist.");
            }
            article.DefaultUnitPrice = request.DefaultUnitPrice;
            article.VatPercentage = request.VatPercentage;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<CostArticleDto>.Ok(await DtoAsync(id, cancellationToken));
        }

        private static Dictionary<string, string[]> PriceErrors(SavePackagingArticleDto request)
        {
            var errors = new Dictionary<string, string[]>();
            if (request?.DefaultUnitPrice is { } price && !CostArticleRules.IsUnitPrice(price))
            {
                errors["defaultUnitPrice"] = new[] { "The price per piece must be 0 or more, with at most 2 decimals." };
            }
            if (request?.VatPercentage is not { } vat || !CostArticleRules.IsVatPercentage(vat))
            {
                errors["vatPercentage"] = new[] { "A VAT percentage from 0 to 100 is required." };
            }
            return errors;
        }

        private async Task<ServiceResult<CostArticleDto>> CreateAsync(Guid? requestId, string requestedName, string kind, decimal? price, decimal? vat,
            Dictionary<string, string[]> errors, CancellationToken cancellationToken)
        {
            if (requestId == null || requestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            var name = CostArticleRules.NormalizeName(requestedName);
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

            var replay = await _db.CostArticles.AsNoTracking().SingleOrDefaultAsync(c => c.RequestId == requestId, cancellationToken);
            if (replay != null)
            {
                return replay.NameKey == CostArticleRules.NameKey(name) && replay.Kind == kind
                    ? ServiceResult<CostArticleDto>.Ok(await DtoAsync(replay.Id, cancellationToken))
                    : ServiceResult<CostArticleDto>.Conflict($"RequestId {requestId} was already used for cost article {replay.Name}.");
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
                RequestId = requestId, Name = name, NameKey = key, Kind = kind, DefaultUnitPrice = price, VatPercentage = vat, IsActive = true, SortOrder = sortOrder,
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
                return await CreateAsync(requestId, requestedName, kind, price, vat, errors, cancellationToken);
            }
            return ServiceResult<CostArticleDto>.Created(await DtoAsync(article.Id, cancellationToken));
        }

        // Inactief: niet meer te kiezen op een nieuwe factuur- of verkoopregel; bestaande regels en partijkosten blijven zoals ze zijn.
        // Alleen binnen de eigen soort (kosten onder Inkoop, emballage onder Stamgegevens).
        public async Task<ServiceResult<CostArticleDto>> SetActiveAsync(int id, SetCostArticleActiveDto request, CancellationToken cancellationToken = default,
            string kind = CostArticleRules.KindCost)
        {
            if (request?.IsActive == null)
            {
                return ServiceResult<CostArticleDto>.Validation(new Dictionary<string, string[]> { ["isActive"] = new[] { "Say whether the cost article is active." } });
            }
            var article = await _db.CostArticles.SingleOrDefaultAsync(c => c.Id == id && c.Kind == kind, cancellationToken);
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
