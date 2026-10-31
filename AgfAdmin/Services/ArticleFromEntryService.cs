using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // "Artikel aanmaken en doorgaan" vanuit Aanvoer plannen of Pallet ontvangen: een nieuwe variant van een bestaand artikel (productsoort
    // + ras, via een sjabloonvariant) met precies de ingetypte herkomst, maat, verpakking, klasse en merk. Een nieuwe productsoort of een
    // nieuw ras blijft in Artikelen. Nooit een half artikel (land uit de vaste lijst verplicht) en nooit een dubbel: bestaat de combinatie
    // al (hoofdletters, accenten en dubbele spaties tellen niet; leeg is "geen"), dan 409 met het bestaande artikel. Idempotent via
    // RequestId: een herhaald verzoek maakt geen tweede artikel. Het artikel en zijn historie-event gaan in één transactie; de aanvoer of
    // ontvangst zelf blijft een eigen, ongewijzigd verzoek met deze variant (de controle "Klopt dit?" vraagt de variant vóór het boeken).
    public sealed class ArticleFromEntryService
    {
        public const string ExistsCode = "article-exists";
        public const string RequestReusedCode = "article-request-reused";

        private const int AdvisoryLockClass = 0x41524645; // eigen lockruimte voor dit gebruik

        private readonly AppDbContext _db;

        public ArticleFromEntryService(AppDbContext db) => _db = db;

        private sealed record Entry(string Source, ProductVariant Template, string CountryCode, string CountryName, string Size, string Packaging,
            string TradeClass, string Brand, decimal? NetKg, string Actor);

        public async Task<ServiceResult<ArticleFromEntryResultDto>> CreateAsync(CreateArticleFromEntryDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A request id is required.");
                return ServiceResult<ArticleFromEntryResultDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.RequestId == request.RequestId, cancellationToken);
            if (replay != null)
            {
                return await ResultForReplayAsync(replay, request, cancellationToken);
            }

            var entry = await ValidateAsync(request, errors, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<ArticleFromEntryResultDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var template = entry.Template;
            var variant = new ProductVariant
            {
                RequestId = request.RequestId,
                ProductId = template.ProductId,
                Product = template.Product,
                Name = template.Name ?? "",
                OriginCountryCode = entry.CountryCode,
                Origin = entry.CountryName,
                SizeCategory = entry.Size,
                Packaging = entry.Packaging,
                TradeClass = entry.TradeClass,
                Brand = entry.Brand,
                // Standaarden van hetzelfde artikel (invoerhulpen, per regel aanpasbaar); het ingevulde kg per verpakking gaat voor.
                WeightMethod = template.WeightMethod,
                PackagingUnit = template.PackagingUnit,
                DefaultNetKgPerPackage = entry.NetKg ?? template.DefaultNetKgPerPackage,
                DefaultPackagesPerPallet = template.DefaultPackagesPerPallet,
                AvailabilityUnit = template.AvailabilityUnit,
                GoodsCode = template.GoodsCode,
                GoodsCodeSystem = template.GoodsCodeSystem,
                DefaultPrice = 0m,
            };

            var strategy = _db.Database.CreateExecutionStrategy();
            ProductVariant existing = null;
            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    existing = null;
                    await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                    // Gelijktijdige verzoeken voor dezelfde productsoort achter elkaar zetten: een lock tot het einde van de transactie en pas
                    // daarna de dubbel-controle, zodat de tweede de eerste ziet (de normalisatie zit in code, niet in een unieke index).
                    if (_db.Database.IsNpgsql())
                    {
                        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockClass}, {entry.Template.ProductId})", cancellationToken);
                    }
                    existing = await FindSameAsync(entry, cancellationToken);
                    if (existing != null)
                    {
                        return;
                    }
                    _db.ProductVariants.Add(variant);
                    await _db.SaveChangesAsync(cancellationToken);
                    var type = entry.Source == ArticleRules.SourceReceipt ? ArticleRules.EventCreatedFromReceipt : ArticleRules.EventCreatedFromSupply;
                    _db.ArticleAuditEvents.Add(ArticleLifecycleService.Event(variant, type, entry.Actor, DateTime.UtcNow, request.RequestId));
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                });
            }
            catch (DbUpdateException)
            {
                // Gelijktijdige herhaling met dezelfde RequestId: de unieke index hield de tweede tegen; dan het opgeslagen artikel.
                _db.ChangeTracker.Clear();
                var stored = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.RequestId == request.RequestId, cancellationToken);
                if (stored == null)
                {
                    throw;
                }
                return await ResultForReplayAsync(stored, request, cancellationToken);
            }
            if (existing != null)
            {
                var message = existing.IsArchived
                    ? $"Dit artikel bestaat al (#{existing.Id}) maar is gearchiveerd; zet het terug in Artikelen."
                    : $"Dit artikel bestaat al (#{existing.Id}); kies dat artikel.";
                return ServiceResult<ArticleFromEntryResultDto>.Conflict(message, new { message, code = ExistsCode, article = ArticleService.ToDto(existing) });
            }
            return ServiceResult<ArticleFromEntryResultDto>.Created(new ArticleFromEntryResultDto { Article = ArticleService.ToDto(variant), Replayed = false });
        }

        private async Task<Entry> ValidateAsync(CreateArticleFromEntryDto request, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            var source = request.Source?.Trim();
            if (source != ArticleRules.SourceSupply && source != ArticleRules.SourceReceipt)
            {
                ValidationErrors.Add(errors, "source", $"Source must be {ArticleRules.SourceSupply} or {ArticleRules.SourceReceipt}.");
            }
            var template = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == request.TemplateVariantId, cancellationToken);
            if (template == null)
            {
                ValidationErrors.Add(errors, "templateVariantId", "Choose an existing article (product and variety); a new product or variety is created in Articles.");
            }
            var country = CountryRules.Find(string.IsNullOrWhiteSpace(request.OriginCountryCode) ? null : request.OriginCountryCode.Trim().ToUpperInvariant());
            if (country == null)
            {
                ValidationErrors.Add(errors, "originCountryCode", "Choose the country of origin from the country list.");
            }
            string Text(string value, int max, string key)
            {
                var text = ArticleRules.TypedText(value);
                if (text != null && text.Length > max)
                {
                    ValidationErrors.Add(errors, key, $"This field may contain at most {max} characters.");
                }
                return text;
            }
            var size = Text(request.SizeCategory, ArticleRules.MaxTextLength, "sizeCategory");
            var packaging = Text(request.Packaging, ArticleRules.MaxTextLength, "packaging");
            var tradeClass = Text(request.TradeClass, ArticleRules.MaxTradeClassLength, "tradeClass");
            var brand = Text(request.Brand, ArticleRules.MaxTextLength, "brand");
            if (request.NetKgPerPackage != null && !SupplyRules.IsPositiveDecimal18_3(request.NetKgPerPackage.Value))
            {
                ValidationErrors.Add(errors, "netKgPerPackage", "Net kg per package must be positive and may have at most 3 decimals.");
            }
            var actor = Text(request.ActorReference, ArticleRules.MaxActorLength, "actorReference");
            return new Entry(source, template, country?.Code, country?.NameNl, size, packaging, tradeClass, brand, request.NetKgPerPackage, actor);
        }

        // Dezelfde combinatie onder hetzelfde artikel (productsoort + ras), ook gearchiveerd: dan nooit een tweede.
        private async Task<ProductVariant> FindSameAsync(Entry entry, CancellationToken cancellationToken)
        {
            var siblings = await _db.ProductVariants.Include(v => v.Product).Where(v => v.ProductId == entry.Template.ProductId).ToListAsync(cancellationToken);
            return siblings.Where(v => ArticleRules.AttributeKey(v.Name) == ArticleRules.AttributeKey(entry.Template.Name) && SameIdentity(v, entry))
                .OrderBy(v => v.IsArchived).ThenBy(v => v.Id).FirstOrDefault();
        }

        private static bool SameIdentity(ProductVariant variant, Entry entry) =>
            (variant.OriginCountryCode != null
                ? variant.OriginCountryCode == entry.CountryCode
                : ArticleRules.AttributeKey(variant.Origin) == ArticleRules.AttributeKey(entry.CountryName))
            && ArticleRules.AttributeKey(variant.SizeCategory) == ArticleRules.AttributeKey(entry.Size)
            && ArticleRules.AttributeKey(variant.Packaging) == ArticleRules.AttributeKey(entry.Packaging)
            && ArticleRules.AttributeKey(variant.TradeClass) == ArticleRules.AttributeKey(entry.TradeClass)
            && ArticleRules.AttributeKey(variant.Brand) == ArticleRules.AttributeKey(entry.Brand);

        // Herhaald verzoek: hetzelfde artikel terug (200). Een RequestId die voor een andere combinatie, een ander artikel (productsoort + ras)
        // of een andere bron werd gebruikt: 409.
        private async Task<ServiceResult<ArticleFromEntryResultDto>> ResultForReplayAsync(ProductVariant stored, CreateArticleFromEntryDto request, CancellationToken cancellationToken)
        {
            var country = string.IsNullOrWhiteSpace(request.OriginCountryCode) ? null : request.OriginCountryCode.Trim().ToUpperInvariant();
            var template = await _db.ProductVariants.AsNoTracking().Where(v => v.Id == request.TemplateVariantId)
                .Select(v => new { v.ProductId, v.Name }).SingleOrDefaultAsync(cancellationToken);
            var source = request.Source?.Trim();
            var storedType = await _db.ArticleAuditEvents.Where(e => e.RequestId == request.RequestId).Select(e => e.EventType).FirstOrDefaultAsync(cancellationToken);
            var expectedType = source == ArticleRules.SourceReceipt ? ArticleRules.EventCreatedFromReceipt : ArticleRules.EventCreatedFromSupply;
            var same = template != null
                && stored.ProductId == template.ProductId
                && ArticleRules.AttributeKey(stored.Name) == ArticleRules.AttributeKey(template.Name)
                && (source == ArticleRules.SourceSupply || source == ArticleRules.SourceReceipt)
                && (storedType == null || storedType == expectedType)
                && stored.OriginCountryCode == country
                && ArticleRules.AttributeKey(stored.SizeCategory) == ArticleRules.AttributeKey(request.SizeCategory)
                && ArticleRules.AttributeKey(stored.Packaging) == ArticleRules.AttributeKey(request.Packaging)
                && ArticleRules.AttributeKey(stored.TradeClass) == ArticleRules.AttributeKey(request.TradeClass)
                && ArticleRules.AttributeKey(stored.Brand) == ArticleRules.AttributeKey(request.Brand);
            if (!same)
            {
                const string message = "This request id was already used for another article; send a new request id.";
                return ServiceResult<ArticleFromEntryResultDto>.Conflict(message, new { message, code = RequestReusedCode });
            }
            return ServiceResult<ArticleFromEntryResultDto>.Ok(new ArticleFromEntryResultDto { Article = ArticleService.ToDto(stored), Replayed = true });
        }
    }
}
