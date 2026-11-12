namespace AgfAdmin.Models
{
    // Een document bij een verkooporder (documenten deel 1: de laadbon voor de loods), per versie. Een versie is een snapshot van de
    // gegevens op dat moment (SnapshotJson); de PDF wordt daaruit gemaakt, zodat een oude versie er altijd hetzelfde uitziet. Append-only:
    // alleen SentAt en SentBy worden één keer gezet (doorgegeven of, in deel 3, verstuurd); verder verandert er niets (regel 31).
    public sealed class SalesOrderDocument
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Kind { get; set; }          // LoadingSlip (SalesOrderDocumentRules)
        public string Number { get; set; }        // LB-2026-0012: het VO-nummer met LB ervoor, gelijk voor alle versies
        public int Version { get; set; }          // 1, 2, ... per order en soort
        public string Reason { get; set; }        // Confirmed (bij bevestigen) of Changed (de order veranderde daarna)
        public string SnapshotJson { get; set; }  // de gegevens van de bon (LoadingSlipSnapshot), zonder prijzen of bedragen
        public string ContentHash { get; set; }   // sha256 van de inhoud: dezelfde inhoud geeft nooit een nieuwe versie
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }

        public DateTime? SentAt { get; set; }
        public string SentBy { get; set; }
    }
}
