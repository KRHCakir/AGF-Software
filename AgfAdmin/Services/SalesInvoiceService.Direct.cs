using AgfAdmin.DTOs;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, directe factuur (zonder order): eigen regels, productregels uit een partij met reservering. Komt in de volgende PR;
    // tot dan heeft een directe factuur geen voorraad en geen nieuwe regels via het bijwerken.
    public sealed partial class SalesInvoiceService
    {
        // Wijzigingen die alleen bij een directe factuur horen (nieuwe regels, vrije omschrijving, leverdatum); null = niets te doen.
        private Task<Action<DateTime>> PrepareDirectChangesAsync(Invoice invoice, UpdateSalesInvoiceDto request, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
        {
            if ((request.NewLines?.Count ?? 0) > 0)
            {
                errors["newLines"] = new[] { "New lines on a direct invoice are not available yet." };
            }
            return Task.FromResult<Action<DateTime>>(null);
        }

        // Reserveringen van een concept vrijgeven (weggooien); zonder directe productregels niets.
        private Task ReleaseInvoiceStockAsync(Invoice invoice, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        // Sloten op de inhoudsregels van de partijen van een directe factuur; zonder directe productregels niets.
        private Task LockInvoiceStockAsync(Invoice invoice, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
