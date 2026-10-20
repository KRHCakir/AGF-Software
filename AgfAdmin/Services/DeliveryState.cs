using System.Linq.Expressions;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // De enige definitie van een actieve afleverbevestiging: een bevestiging zonder opgeslagen intrekking. Gebruikt door aflevertotalen,
    // DTO's, afleverservice, beide correctieservices en de guard. In een query vertaalt EF dit naar een controle op de intrekkingstabel;
    // op een geladen graaf moet de intrekking zijn meegeladen (zie de Include-helpers in de lezers). Elk leespad heeft een test dat een
    // ingetrokken bevestiging niet meetelt.
    public static class DeliveryState
    {
        public static readonly Expression<Func<ShipmentDeliveryConfirmation, bool>> IsActive = c => c.Retraction == null;

        private static readonly Func<ShipmentDeliveryConfirmation, bool> IsActiveCompiled = IsActive.Compile();

        public static bool Active(ShipmentDeliveryConfirmation confirmation) => confirmation != null && IsActiveCompiled(confirmation);

        // De actieve bevestiging van een verzending, of null. Meer dan één actieve is een schending van het model (guard en index).
        public static ShipmentDeliveryConfirmation ActiveOf(IEnumerable<ShipmentDeliveryConfirmation> confirmations) =>
            confirmations?.Where(IsActiveCompiled).SingleOrDefault();
    }
}
