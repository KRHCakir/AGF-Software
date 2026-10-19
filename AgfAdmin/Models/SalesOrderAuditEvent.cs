namespace AgfAdmin.Models
{
    // Append-only auditrecord op orderniveau (bijvoorbeeld annulering). Bewust los van PalletAuditEvent: dat record
    // vereist een pallet of inhoudsregel als doel en heeft een unieke RequestId per event, terwijl één ordercommando
    // meerdere palletevents veroorzaakt. Snapshots zijn begrensd (geen onbegrensde lijsten van reserverings-id's).
    public sealed class SalesOrderAuditEvent
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }

        public DateTime OccurredAt { get; set; }

        // Idempotentiesleutel van het commando dat dit event veroorzaakte; uniek.
        public Guid? RequestId { get; set; }
    }
}
