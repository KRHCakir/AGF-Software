namespace AgfAdmin.Models
{
    // Lichte koppeling tussen een inkooporder en een factuur- of creditnotanummer van de leverancier, vóór de volledige
    // factuurkoppeling. Meerdere facturen per order en meerdere orders per factuurnummer zijn toegestaan; hetzelfde nummer
    // van dezelfde soort hangt maar één keer aan een order. Append-only: een vastgelegde koppeling wordt niet gewijzigd.
    public sealed class SupplyCommitmentInvoiceRef
    {
        public int Id { get; set; }

        // Idempotentiesleutel van de client: een retry legt nooit een tweede koppeling vast.
        public Guid RequestId { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public string Kind { get; set; }            // Invoice of CreditNote
        public string InvoiceNumber { get; set; }   // zoals op het document (getrimd)
        public DateOnly? InvoiceDate { get; set; }
        public decimal? AmountExclVat { get; set; }

        // Voor later: de ingevoerde inkoopfactuur zelf. Nu altijd leeg; nooit automatisch gezocht of gekoppeld.
        public int? IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }

        public string ActorReference { get; set; }  // optioneel, zoals bij ontvangst
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
