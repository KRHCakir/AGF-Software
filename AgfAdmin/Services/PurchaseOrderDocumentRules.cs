using System.Text.Json;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Eén veld van een auditregel (PurchaseOrderChange): kopvelden zonder lineId; regels met lineId en action added, changed of removed.
    // Oud en nieuw als vaste tekst (getallen invariant), zodat de geschiedenis altijd hetzelfde leest.
    public sealed record PurchaseOrderChangeField(string Field, int? LineId, string Action, string Old, string New);

    // Documentstatus van een inkooporder (stap 4a), los van de ontvangststatus: Open (aanpasbaar) of Definitive (alleen nog een
    // correctie met reden). Gespiegeld in agf-frontend/src/lib/purchaseOrderDocument.ts.
    public static class PurchaseOrderDocumentRules
    {
        public const string StatusOpen = "Open";
        public const string StatusDefinitive = "Definitive";
        public static readonly IReadOnlyList<string> Statuses = new[] { StatusOpen, StatusDefinitive };

        // Waardoor een order definitief werd: de knop Definitief maken, of een gekoppelde factuur (stap 4b; bij de migratie de
        // bestaande factuurnummers op de order).
        public const string ViaManual = "Manual";
        public const string ViaInvoice = "Invoice";

        public const string KindEdit = "Edit";
        public const string KindFinalize = "Finalize";
        public const string KindCorrection = "Correction";
        public static readonly IReadOnlyList<string> Kinds = new[] { KindEdit, KindFinalize, KindCorrection };

        public const string ActionAdded = "added";
        public const string ActionChanged = "changed";
        public const string ActionRemoved = "removed";

        public const int MaxReasonLength = 500;
        public const int MaxActorLength = 100;

        // Wat een correctie na definitief mag wijzigen: de prijs per regel, het ordernummer van de leverancier en de notitie. Aantallen
        // en artikelen volgen uit de ontvangst en blijven vast.
        public const string FieldSupplierReference = "supplierReference";
        public const string FieldNote = "note";
        public const string FieldUnitPrice = "unitPrice";
        public static readonly IReadOnlyList<string> CorrectionFields = new[] { FieldSupplierReference, FieldNote, FieldUnitPrice };

        public static bool IsStatus(string value) => value != null && Statuses.Contains(value, StringComparer.Ordinal);
        public static bool IsDefinitive(SupplyCommitment order) => order.DocumentStatus == StatusDefinitive;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public static string Serialize(IReadOnlyList<PurchaseOrderChangeField> fields) => JsonSerializer.Serialize(fields, Json);
        public static IReadOnlyList<PurchaseOrderChangeField> Deserialize(string json) =>
            string.IsNullOrWhiteSpace(json) ? Array.Empty<PurchaseOrderChangeField>() : JsonSerializer.Deserialize<List<PurchaseOrderChangeField>>(json, Json) ?? new List<PurchaseOrderChangeField>();

        // Maakt een (opgeslagen, gevolgde) Open order definitief en geeft de bijbehorende auditregel terug; de aanroeper voegt die toe en
        // slaat op in zijn eigen transactie. Is de order al definitief, dan verandert er niets (null): een tweede factuur of een herhaalde
        // klik laat de eerste vastlegging staan.
        //
        // Stap 4b (factuur koppelen aan de bon) roept dit aan met ViaInvoice zodra een factuur aan de order gekoppeld wordt, via
        // PurchaseOrderService.FinalizeForInvoiceAsync. De bestaande factuurnummers ("Factuur ontvangen", het AI-voorstel) doen dat in
        // deze stap nog niet; alleen de migratie zette orders met een factuurnummer al op definitief.
        public static PurchaseOrderChange Finalize(SupplyCommitment order, string via, string actor, DateTime now, Guid requestId)
        {
            if (via != ViaManual && via != ViaInvoice) throw new ArgumentException($"A purchase order becomes definitive via {ViaManual} or {ViaInvoice}, not '{via}'.", nameof(via));
            if (IsDefinitive(order))
            {
                return null;
            }
            order.DocumentStatus = StatusDefinitive;
            order.FinalizedAt = now;
            order.FinalizedBy = actor;
            order.FinalizedVia = via;
            order.EditVersion++;
            order.UpdatedAt = now;
            return new PurchaseOrderChange
            {
                RequestId = requestId, SupplyCommitmentId = order.Id, Kind = KindFinalize, ActorReference = actor, CreatedAt = now,
                ChangesJson = Serialize(new[] { new PurchaseOrderChangeField("documentStatus", null, ActionChanged, StatusOpen, StatusDefinitive) }),
            };
        }

        // De plek waar de periodeafsluiting (later, zie het plan voor vergrendelen) een correctie kan weigeren: een reden in gewone taal,
        // of null als corrigeren mag. Nu is er nog geen afgesloten periode, dus altijd null.
        public static string CorrectionBlockedReason(SupplyCommitment order, DateOnly today) => null;
    }
}
