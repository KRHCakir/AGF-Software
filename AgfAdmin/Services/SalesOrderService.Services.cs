using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Dienstregels op een verkooporder (feedback 22 november): doorberekende handling of transport met een Dienst-artikel, aantal, prijs,
    // korting en het btw-tarief van het artikel (standaard 21%). Ze staan los van de goederenregels: geen reservering, geen voorraad, geen
    // pallets of kg, en niet op de laadbon of de transportopdracht. Wel in de totalen en de btw per tarief (en later op de factuur).
    public sealed partial class SalesOrderService
    {
        public const int MaxServiceLines = 50;

        private static List<SalesOrderServiceLine> BuildServiceLines(List<CreateSalesOrderServiceLineDto> request, Dictionary<string, List<string>> errors)
        {
            var lines = new List<SalesOrderServiceLine>();
            if (request == null)
            {
                return lines;
            }
            if (request.Count > MaxServiceLines)
            {
                ValidationErrors.Add(errors, "serviceLines", $"At most {MaxServiceLines} service lines are allowed.");
                return lines;
            }
            for (var i = 0; i < request.Count; i++)
            {
                var key = $"serviceLines[{i}]";
                var line = request[i];
                if (line == null)
                {
                    ValidationErrors.Add(errors, key, "A line is required.");
                    continue;
                }
                var before = errors.Count;
                if (line.CostArticleId == null)
                {
                    ValidationErrors.Add(errors, $"{key}.costArticleId", "A service article is required.");
                }
                if (line.Quantity is not { } quantity || !SupplyRules.IsPositiveDecimal18_3(quantity))
                {
                    ValidationErrors.Add(errors, $"{key}.quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
                }
                OrderLineMath.Validate(line.UnitPrice, line.DiscountPercentage, line.VatPercentage, key, errors, ValidationErrors.Add);
                if (errors.Count == before)
                {
                    lines.Add(new SalesOrderServiceLine
                    {
                        CostArticleId = line.CostArticleId!.Value, Quantity = line.Quantity!.Value, UnitPrice = line.UnitPrice, DiscountPercentage = line.DiscountPercentage,
                        // Weggelaten btw: het tarief van het artikel, gezet in ResolveServicesAsync (-1 als markering tot dan).
                        VatPercentage = line.VatPercentage ?? -1m,
                    });
                }
            }
            return lines;
        }

        // Elk artikel bestaat, is een Dienst (soort Cost) en is actief; zonder opgegeven btw geldt het tarief van het artikel (standaard 21%).
        // Na de replaycontrole, zodat een eerder opgeslagen order ook na het inactief maken van een artikel herkend wordt.
        private async Task<ServiceResult<SalesOrderDto>> ResolveServicesAsync(SalesOrder candidate, CancellationToken cancellationToken)
        {
            var lines = candidate.ServiceLines.ToList();
            if (lines.Count == 0)
            {
                return null;
            }
            var ids = lines.Select(l => l.CostArticleId).Distinct().ToList();
            var articles = await _db.CostArticles.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
            var errors = new Dictionary<string, List<string>>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!articles.TryGetValue(lines[i].CostArticleId, out var article) || article.Kind != CostArticleRules.KindCost)
                {
                    ValidationErrors.Add(errors, $"serviceLines[{i}].costArticleId", $"Article {lines[i].CostArticleId} is not a service article.");
                }
                else if (!article.IsActive)
                {
                    ValidationErrors.Add(errors, $"serviceLines[{i}].costArticleId", $"Service article {article.Name} is inactive.");
                }
                else
                {
                    if (lines[i].VatPercentage < 0m) lines[i].VatPercentage = article.VatPercentage ?? ArticleRules.DefaultServiceVat;
                    lines[i].CreatedAt = candidate.CreatedAt;
                }
            }
            return errors.Count > 0 ? ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)) : null;
        }

        // Replay: dezelfde diensten in dezelfde volgorde (artikel, aantal, prijs, korting, en de btw als die was opgegeven).
        private static bool SameServices(SalesOrder existing, SalesOrder candidate)
        {
            var stored = existing.ServiceLines.OrderBy(l => l.Id).ToList();
            var asked = candidate.ServiceLines.ToList();
            return stored.Count == asked.Count && stored.Zip(asked).All(pair => pair.First.CostArticleId == pair.Second.CostArticleId
                && pair.First.Quantity == pair.Second.Quantity && pair.First.UnitPrice == pair.Second.UnitPrice
                && pair.First.DiscountPercentage == pair.Second.DiscountPercentage && (pair.Second.VatPercentage < 0m || pair.First.VatPercentage == pair.Second.VatPercentage));
        }

        private static SalesOrderServiceLineDto ToServiceDto(SalesOrderServiceLine line) => new()
        {
            Id = line.Id,
            CostArticleId = line.CostArticleId,
            Name = line.CostArticle?.Name,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            DiscountPercentage = line.DiscountPercentage,
            VatPercentage = line.VatPercentage,
            LineTotal = OrderLineMath.LineAmount(line.Quantity, line.UnitPrice, line.DiscountPercentage),
        };
    }
}
