using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Emballageregels op een verkooporder (pallets, kratten): artikel, heel aantal en prijs per stuk; de btw komt van het artikel. Ze staan
    // los van de goederenregels en doen dus niets met reserveren, vrije voorraad, tekort of verzenden.
    public sealed partial class SalesOrderService
    {
        private static List<SalesOrderPackagingLine> BuildPackagingLines(List<CreateSalesOrderPackagingLineDto> request, Dictionary<string, List<string>> errors)
        {
            var lines = new List<SalesOrderPackagingLine>();
            if (request == null)
            {
                return lines;
            }
            if (request.Count > CostArticleRules.MaxPackagingLines)
            {
                ValidationErrors.Add(errors, "packagingLines", $"At most {CostArticleRules.MaxPackagingLines} packaging lines are allowed.");
                return lines;
            }
            for (var i = 0; i < request.Count; i++)
            {
                var key = $"packagingLines[{i}]";
                var line = request[i];
                if (line == null)
                {
                    ValidationErrors.Add(errors, key, "A line is required.");
                    continue;
                }
                var before = errors.Count;
                if (line.CostArticleId == null)
                {
                    ValidationErrors.Add(errors, $"{key}.costArticleId", "A packaging article is required.");
                }
                if (line.Quantity is not { } quantity || quantity < 1m || quantity > CostArticleRules.MaxPackagingQuantity || decimal.Truncate(quantity) != quantity)
                {
                    ValidationErrors.Add(errors, $"{key}.quantity", $"The quantity must be a whole number from 1 to {CostArticleRules.MaxPackagingQuantity}.");
                }
                if (line.UnitPrice is { } price && !CostArticleRules.IsUnitPrice(price))
                {
                    ValidationErrors.Add(errors, $"{key}.unitPrice", "The price per piece must be 0 or more, with at most 2 decimals.");
                }
                if (errors.Count == before)
                {
                    lines.Add(new SalesOrderPackagingLine { CostArticleId = line.CostArticleId!.Value, Quantity = (int)line.Quantity!.Value, UnitPrice = line.UnitPrice });
                }
            }
            return lines;
        }

        // Elk artikel bestaat, is emballage en is actief; de btw van het artikel gaat mee op de regel. Na de replaycontrole, zodat een eerder
        // opgeslagen order ook na het inactief maken van een artikel herkend wordt.
        private async Task<ServiceResult<SalesOrderDto>> ResolvePackagingAsync(SalesOrder candidate, CancellationToken cancellationToken)
        {
            var lines = candidate.PackagingLines.ToList();
            if (lines.Count == 0)
            {
                return null;
            }
            var ids = lines.Select(l => l.CostArticleId).Distinct().ToList();
            var articles = await _db.CostArticles.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
            var errors = new Dictionary<string, List<string>>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!articles.TryGetValue(lines[i].CostArticleId, out var article) || article.Kind != CostArticleRules.KindPackaging)
                {
                    ValidationErrors.Add(errors, $"packagingLines[{i}].costArticleId", $"Article {lines[i].CostArticleId} is not a packaging article.");
                }
                else if (!article.IsActive)
                {
                    ValidationErrors.Add(errors, $"packagingLines[{i}].costArticleId", $"Packaging article {article.Name} is inactive.");
                }
                else
                {
                    lines[i].VatPercentage = article.VatPercentage ?? CostArticleRules.StandardPackagingVat;
                    lines[i].CreatedAt = candidate.CreatedAt;
                }
            }
            return errors.Count > 0 ? ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)) : null;
        }

        // Replay: dezelfde emballage in dezelfde volgorde (artikel, aantal, prijs). De btw volgt uit het artikel en telt niet mee.
        private static bool SamePackaging(SalesOrder existing, SalesOrder candidate)
        {
            var stored = existing.PackagingLines.OrderBy(l => l.Id).ToList();
            var asked = candidate.PackagingLines.ToList();
            return stored.Count == asked.Count && stored.Zip(asked).All(pair => pair.First.CostArticleId == pair.Second.CostArticleId
                && pair.First.Quantity == pair.Second.Quantity && pair.First.UnitPrice == pair.Second.UnitPrice);
        }

        private static SalesOrderPackagingLineDto ToPackagingDto(SalesOrderPackagingLine line) => new()
        {
            Id = line.Id,
            CostArticleId = line.CostArticleId,
            Name = line.CostArticle?.Name,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            VatPercentage = line.VatPercentage,
            LineTotal = line.UnitPrice * line.Quantity,
        };
    }
}
