using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Kleur (feedback 22 november): de kleurenlijst per product onder Stamgegevens, en de kleur van een inkoop-partij (bij de keuring of op
    // het partijdetail). Een kleur moet in de lijst staan van een product van die partij; leeg mag altijd (kleur weghalen). Elke wijziging
    // van de partijkleur krijgt een historieregel met oud en nieuw (regel 43).
    public sealed class LotColorService(AppDbContext db, ICurrentUser user = null)
    {
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;

        public async Task<ServiceResult<ProductColorsDto>> GetProductColorsAsync(int productId, CancellationToken cancellationToken = default)
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            return product == null ? ServiceResult<ProductColorsDto>.NotFound($"Product {productId} does not exist.")
                : ServiceResult<ProductColorsDto>.Ok(new ProductColorsDto { ProductId = product.Id, ProductName = product.Name, Colors = LotColorRules.Parse(product.ColorValues) });
        }

        // De lijst vervangen. Een kleur die al op een partij staat, blijft daar staan (historie); alleen nieuwe keuzes volgen de lijst.
        public async Task<ServiceResult<ProductColorsDto>> UpdateProductColorsAsync(int productId, ProductColorsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            var colors = LotColorRules.Normalize(request?.Colors, errors);
            if (colors == null) return ServiceResult<ProductColorsDto>.Validation(errors);
            var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product == null) return ServiceResult<ProductColorsDto>.NotFound($"Product {productId} does not exist.");
            product.ColorValues = LotColorRules.Join(colors);
            await db.SaveChangesAsync(cancellationToken);
            return await GetProductColorsAsync(productId, cancellationToken);
        }

        // De kleuren die een partij mag hebben: de lijsten van de producten van haar inkoopregels en palletregels samen.
        internal static async Task<List<string>> AllowedAsync(AppDbContext db, int partijId, CancellationToken cancellationToken)
        {
            var productIds = await db.SupplyCommitmentLines.AsNoTracking().Where(l => l.PartijId == partijId).Select(l => l.ProductVariant.ProductId)
                .Concat(db.PalletContentLines.AsNoTracking().Where(c => c.PartijId == partijId).Select(c => c.SupplyCommitmentLine.ProductVariant.ProductId))
                .Distinct().ToListAsync(cancellationToken);
            var lists = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).OrderBy(p => p.Name).Select(p => p.ColorValues).ToListAsync(cancellationToken);
            return lists.SelectMany(LotColorRules.Parse).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<ServiceResult<PartijSummaryDto>> SetPartijColorAsync(int partijId, SetPartijColorDto request, CancellationToken cancellationToken = default)
        {
            var partij = await db.Partijen.SingleOrDefaultAsync(p => p.Id == partijId, cancellationToken);
            if (partij == null) return ServiceResult<PartijSummaryDto>.NotFound($"Lot {partijId} does not exist.");
            if (partij.Kind != PartijRules.KindPurchase)
            {
                return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["color"] = new[] { "Only a purchase lot has a color." } });
            }
            var allowed = await AllowedAsync(db, partijId, cancellationToken);
            var wanted = string.IsNullOrWhiteSpace(request?.Color) ? null : request.Color.Trim();
            var color = wanted == null ? null : allowed.FirstOrDefault(c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase));
            if (wanted != null && color == null)
            {
                var message = allowed.Count == 0 ? "This product has no colors; add them under Master data first."
                    : $"Choose one of the colors of this product: {string.Join(", ", allowed)}.";
                return ServiceResult<PartijSummaryDto>.Validation(new Dictionary<string, string[]> { ["color"] = new[] { message } });
            }
            if (!string.Equals(partij.Color, color, StringComparison.Ordinal))
            {
                db.PartijAuditEvents.Add(new PartijAuditEvent
                {
                    PartijId = partij.Id, EventType = PartijRules.EventColorChanged, OldValue = partij.Color, NewValue = color,
                    ActorReference = _user.Actor(PartijRules.MaxActorLength), OccurredAt = DateTime.UtcNow
                });
                partij.Color = color;
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ServiceResult<PartijSummaryDto>.Conflict($"The color of lot {partij.Number} was changed at the same time; reload and try again.");
                }
            }
            return ServiceResult<PartijSummaryDto>.Ok(await new PartijService(db, _user).SummaryAsync(partij, cancellationToken));
        }
    }
}
