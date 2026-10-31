using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Levensloop van een artikel (ProductVariant): verwijderen als er nergens iets aan hangt, anders archiveren (inactief) en weer
    // terugzetten. Elke actie krijgt een append-only historie-event met de volledige naam van dat moment. Guard-regel 19 bewaakt
    // dezelfde verwijzingen bij elke save, ook voor andere writers.
    public sealed class ArticleLifecycleService
    {
        public const string InUseCode = "article-in-use";
        public const string ProductHasArticlesCode = "product-has-articles";

        // Soorten verwijzingen in vaste volgorde; dezelfde sleutels gebruikt de frontend voor de tekst ("3 ontvangsten").
        public const string UsagePurchaseOrderLines = "purchaseOrderLines";
        public const string UsageReceipts = "receipts";
        public const string UsageReceiptDeviations = "receiptDeviations";
        public const string UsageSalesOrderLines = "salesOrderLines";
        public const string UsagePurchaseInvoiceLines = "purchaseInvoiceLines";
        public const string UsageInvoiceLines = "invoiceLines";
        public const string UsageLegacyOrderLines = "legacyOrderLines";
        public const string UsageInventory = "inventory";
        public const string UsageWasteLogs = "wasteLogs";
        public static readonly IReadOnlyList<string> UsageKinds = new[]
        {
            UsagePurchaseOrderLines, UsageReceipts, UsageReceiptDeviations, UsageSalesOrderLines, UsagePurchaseInvoiceLines, UsageInvoiceLines,
            UsageLegacyOrderLines, UsageInventory, UsageWasteLogs,
        };

        private static readonly IReadOnlyDictionary<string, (string One, string Many)> UsageWords = new Dictionary<string, (string, string)>
        {
            [UsagePurchaseOrderLines] = ("inkooporderregel", "inkooporderregels"),
            [UsageReceipts] = ("ontvangst", "ontvangsten"),
            [UsageReceiptDeviations] = ("afwijking bij ontvangst", "afwijkingen bij ontvangst"),
            [UsageSalesOrderLines] = ("verkooporderregel", "verkooporderregels"),
            [UsagePurchaseInvoiceLines] = ("inkoopfactuurregel", "inkoopfactuurregels"),
            [UsageInvoiceLines] = ("factuurregel", "factuurregels"),
            [UsageLegacyOrderLines] = ("oude bestelregel", "oude bestelregels"),
            [UsageInventory] = ("oude voorraadregel", "oude voorraadregels"),
            [UsageWasteLogs] = ("afvalregistratie", "afvalregistraties"),
        };

        private readonly AppDbContext _db;

        public ArticleLifecycleService(AppDbContext db) => _db = db;

        public async Task<ServiceResult<ArticleUsageDto>> UsageAsync(int id, CancellationToken cancellationToken = default)
        {
            if (!await _db.ProductVariants.AnyAsync(v => v.Id == id, cancellationToken))
            {
                return ServiceResult<ArticleUsageDto>.NotFound($"Artikel {id} bestaat niet.");
            }
            return ServiceResult<ArticleUsageDto>.Ok(await CountUsageAsync(id, cancellationToken));
        }

        // Alle verwijzingen naar een variant, ook de oude tabellen die bij verwijderen anders stil zouden meeverdwijnen (cascade).
        internal async Task<ArticleUsageDto> CountUsageAsync(int id, CancellationToken cancellationToken)
        {
            var counts = new Dictionary<string, int>
            {
                [UsagePurchaseOrderLines] = await _db.SupplyCommitmentLines.CountAsync(l => l.ProductVariantId == id, cancellationToken),
                [UsageReceipts] = await _db.PalletContentLines.CountAsync(c => c.SupplyCommitmentLine.ProductVariantId == id, cancellationToken),
                [UsageReceiptDeviations] = await _db.SupplyReceiptDeviations.CountAsync(d => d.ExpectedProductVariantId == id || d.ReceivedProductVariantId == id, cancellationToken),
                [UsageSalesOrderLines] = await _db.SalesOrderLines.CountAsync(l => l.ProductVariantId == id, cancellationToken),
                [UsagePurchaseInvoiceLines] = await _db.IncomingInvoiceLines.CountAsync(l => l.ProductVariantId == id, cancellationToken),
                [UsageInvoiceLines] = await _db.Set<InvoiceLine>().CountAsync(l => l.ProductVariantId == id, cancellationToken),
                [UsageLegacyOrderLines] = await _db.OrderLines.CountAsync(l => l.ProductVariantId == id, cancellationToken),
                [UsageInventory] = await _db.Inventory.CountAsync(i => i.ProductVariantId == id, cancellationToken),
                [UsageWasteLogs] = await _db.WasteLogs.CountAsync(w => w.ProductVariantId == id, cancellationToken),
            };
            var usages = UsageKinds.Where(kind => counts[kind] > 0).Select(kind => new ArticleUsageCountDto { Kind = kind, Count = counts[kind] }).ToList();
            return new ArticleUsageDto { ArticleId = id, CanDelete = usages.Count == 0, Usages = usages, Message = usages.Count == 0 ? null : InUseMessage(usages) };
        }

        // "Dit artikel is gebruikt in 3 ontvangsten en 1 inkooporderregel en kan niet verwijderd worden. Archiveer het in plaats daarvan."
        public static string InUseMessage(IReadOnlyList<ArticleUsageCountDto> usages)
        {
            var parts = usages.Select(u => $"{u.Count} {(u.Count == 1 ? UsageWords[u.Kind].One : UsageWords[u.Kind].Many)}").ToList();
            var list = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} en {parts[^1]}";
            return $"Dit artikel is gebruikt in {list} en kan niet verwijderd worden. Archiveer het in plaats daarvan.";
        }

        // Verwijderen: alleen zonder enig gebruik (anders 409 met de reden). Was dit het laatste artikel van de productsoort, dan
        // verdwijnt die soort mee (geen lege soorten achterlaten). De historie blijft met de naam van toen.
        public async Task<ServiceResult<bool>> DeleteAsync(int id, ArticleActionDto request, CancellationToken cancellationToken = default)
        {
            var actor = Actor(request, out var actorError);
            if (actorError != null)
            {
                return ServiceResult<bool>.Validation(actorError);
            }
            var variant = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            if (variant == null)
            {
                return ServiceResult<bool>.NotFound($"Artikel {id} bestaat niet.");
            }
            var usage = await CountUsageAsync(id, cancellationToken);
            if (!usage.CanDelete)
            {
                return ServiceResult<bool>.Conflict(usage.Message, new { message = usage.Message, code = InUseCode, usages = usage.Usages });
            }

            var now = DateTime.UtcNow;
            _db.ArticleAuditEvents.Add(Event(variant, ArticleRules.EventDeleted, actor, now));
            _db.ProductVariants.Remove(variant);
            var lastOfProduct = !await _db.ProductVariants.AnyAsync(v => v.ProductId == variant.ProductId && v.Id != id, cancellationToken);
            if (lastOfProduct)
            {
                _db.ArticleAuditEvents.Add(new ArticleAuditEvent
                {
                    ProductId = variant.ProductId, EventType = ArticleRules.EventProductDeleted, Description = variant.Product.Name?.Trim() ?? "",
                    ActorReference = actor, OccurredAt = now,
                });
                _db.Products.Remove(variant.Product);
            }
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or TraceabilityViolationException)
            {
                // Gelijktijdig in gebruik genomen: de foreign key of guard-regel 19 hield de verwijdering tegen.
                const string message = "Dit artikel is intussen in gebruik genomen en kan niet verwijderd worden. Archiveer het in plaats daarvan.";
                return ServiceResult<bool>.Conflict(message, new { message, code = InUseCode });
            }
            return ServiceResult<bool>.Ok(true);
        }

        // Archiveren: verdwijnt uit keuzelijsten en suggesties, blijft in de historie. Al gearchiveerd = niets te doen (geen tweede event).
        public Task<ServiceResult<ArticleDto>> ArchiveAsync(int id, ArticleActionDto request, CancellationToken cancellationToken = default) =>
            SetArchivedAsync(id, true, request, cancellationToken);

        public Task<ServiceResult<ArticleDto>> RestoreAsync(int id, ArticleActionDto request, CancellationToken cancellationToken = default) =>
            SetArchivedAsync(id, false, request, cancellationToken);

        private async Task<ServiceResult<ArticleDto>> SetArchivedAsync(int id, bool archived, ArticleActionDto request, CancellationToken cancellationToken)
        {
            var actor = Actor(request, out var actorError);
            if (actorError != null)
            {
                return ServiceResult<ArticleDto>.Validation(actorError);
            }
            var variant = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            if (variant == null)
            {
                return ServiceResult<ArticleDto>.NotFound($"Artikel {id} bestaat niet.");
            }
            if (variant.IsArchived == archived)
            {
                return ServiceResult<ArticleDto>.Ok(ArticleService.ToDto(variant));
            }
            var now = DateTime.UtcNow;
            variant.IsArchived = archived;
            variant.ArchivedAt = archived ? now : null;
            variant.ArchivedBy = archived ? actor : null;
            _db.ArticleAuditEvents.Add(Event(variant, archived ? ArticleRules.EventArchived : ArticleRules.EventRestored, actor, now));
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<ArticleDto>.Ok(ArticleService.ToDto(variant));
        }

        public async Task<IReadOnlyList<EmptyProductDto>> EmptyProductsAsync(CancellationToken cancellationToken = default) =>
            await _db.Products.AsNoTracking().Where(p => !_db.ProductVariants.Any(v => v.ProductId == p.Id))
                .OrderBy(p => p.Name).Select(p => new EmptyProductDto { Id = p.Id, Name = p.Name }).ToListAsync(cancellationToken);

        // Een productsoort verwijderen: alleen als er geen artikelen (varianten) meer onder hangen; de artikelen zelf volgen hun eigen controle.
        public async Task<ServiceResult<bool>> DeleteProductAsync(int productId, ArticleActionDto request, CancellationToken cancellationToken = default)
        {
            var actor = Actor(request, out var actorError);
            if (actorError != null)
            {
                return ServiceResult<bool>.Validation(actorError);
            }
            var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product == null)
            {
                return ServiceResult<bool>.NotFound($"Productsoort {productId} bestaat niet.");
            }
            var articles = await _db.ProductVariants.CountAsync(v => v.ProductId == productId, cancellationToken);
            if (articles > 0)
            {
                var message = $"Productsoort {product.Name} heeft nog {articles} {(articles == 1 ? "artikel" : "artikelen")} en kan niet verwijderd worden. Verwijder of archiveer eerst de artikelen.";
                return ServiceResult<bool>.Conflict(message, new { message, code = ProductHasArticlesCode, articleCount = articles });
            }
            _db.ArticleAuditEvents.Add(new ArticleAuditEvent
            {
                ProductId = productId, EventType = ArticleRules.EventProductDeleted, Description = product.Name?.Trim() ?? "", ActorReference = actor, OccurredAt = DateTime.UtcNow,
            });
            _db.Products.Remove(product);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or TraceabilityViolationException)
            {
                var message = $"Productsoort {product.Name} heeft intussen een artikel gekregen en kan niet verwijderd worden.";
                return ServiceResult<bool>.Conflict(message, new { message, code = ProductHasArticlesCode });
            }
            return ServiceResult<bool>.Ok(true);
        }

        internal static ArticleAuditEvent Event(ProductVariant variant, string type, string actor, DateTime at, Guid? requestId = null) => new()
        {
            ProductVariantId = variant.Id == 0 ? null : variant.Id,
            ProductId = variant.ProductId == 0 ? variant.Product?.Id : variant.ProductId,
            EventType = type,
            Description = ArticleRules.FullName(variant.Product?.Name, variant.Name, variant.Origin, variant.SizeCategory, variant.Packaging, variant.TradeClass, variant.Brand),
            ActorReference = actor,
            OccurredAt = at,
            RequestId = requestId,
        };

        private static string Actor(ArticleActionDto request, out IReadOnlyDictionary<string, string[]> error)
        {
            error = null;
            var actor = string.IsNullOrWhiteSpace(request?.ActorReference) ? null : request.ActorReference.Trim();
            if (actor != null && actor.Length > ArticleRules.MaxActorLength)
            {
                error = new Dictionary<string, string[]> { ["actorReference"] = new[] { $"Een naam is hoogstens {ArticleRules.MaxActorLength} tekens." } };
            }
            return actor;
        }
    }
}
