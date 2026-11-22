using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public sealed partial class PurchaseOrderService
    {
        // "Herkomst controleren" (feedback 22 november): inkooporders met een herkomsttekst zonder land, met de landen die de tekst zou
        // kunnen zijn als voorstel. Er wordt nooit automatisch gekozen. Een Open order kies je het land via Aanpassen; bij een definitieve
        // order blijft de oude tekst staan (de kop verandert daar niet meer, regel 29) en is de regel alleen ter informatie.
        public async Task<IReadOnlyList<PurchaseOrderOriginCheckDto>> OriginCheckAsync(CancellationToken cancellationToken = default) =>
            (await _db.SupplyCommitments.AsNoTracking()
                    .Where(c => c.OriginCountryCode == null && c.Origin != "" && c.Status != SupplyRules.StatusCancelled)
                    .OrderByDescending(c => c.Id)
                    .Select(c => new { c.Id, c.OrderNumber, VendorName = c.Vendor.Name, c.Origin, c.DocumentStatus, c.Route })
                    .ToListAsync(cancellationToken))
                .Where(c => !string.IsNullOrWhiteSpace(c.Origin))
                .Select(c => new PurchaseOrderOriginCheckDto
                {
                    Id = c.Id, OrderNumber = c.OrderNumber, VendorName = c.VendorName, Origin = c.Origin.Trim(),
                    Definitive = c.DocumentStatus == PurchaseOrderDocumentRules.StatusDefinitive || c.Route != SupplyRules.RouteWarehouse,
                    Suggestions = CountryRules.Matches(c.Origin).Concat(CountryRules.Matches(System.Text.RegularExpressions.Regex.Replace(c.Origin, @"\s*\(.*\)\s*$", "")))
                        .Distinct().ToList(),
                })
                .ToList();
    }
}
