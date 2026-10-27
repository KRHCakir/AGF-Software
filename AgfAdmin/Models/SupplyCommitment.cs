namespace AgfAdmin.Models
{
    // Commerciële toezegging van een leverancier (verwacht/onderweg). Nooit fysieke voorraad op zich.
    public sealed class SupplyCommitment
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel: een retry maakt nooit een tweede toezegging.
        public Guid RequestId { get; set; }

        public int VendorId { get; set; }
        public Vendor Vendor { get; set; }

        // Klant van een DirectTrade (verplicht); leeg bij Warehouse. Afgedwongen door een check-constraint.
        public int? CustomerId { get; set; }
        public Customer Customer { get; set; }

        public string Route { get; set; }       // "Warehouse" of "DirectTrade"
        public string Status { get; set; }      // "Expected", "PartiallyReceived", "Received", "Cancelled", "Delivered", "Rejected"
        public string Origin { get; set; }
        public string Destination { get; set; } // fysieke bestemming; verplicht voor DirectTrade (afgedwongen in servicecode)
        public DateTime? ExpectedAt { get; set; }

        // Eigen inkoopordernummer "IO-2026-0001": doorlopend per jaar (Europe/Amsterdam) en uniek, bij aanmaak toegekend.
        // OrderYear + OrderSequence zijn de bron (uniek samen); OrderNumber is de vaste weergave daarvan.
        public string OrderNumber { get; set; }
        public int OrderYear { get; set; }
        public int OrderSequence { get; set; }
        // Ordernummer of referentie van de leverancier (vrije tekst, bijvoorbeeld uit het telefoongesprek); optioneel.
        public string SupplierReference { get; set; }
        // Besteldatum (kalenderdatum); standaard vandaag bij aanmaak, bij bestaande orders de aanmaakdatum.
        public DateOnly OrderedOn { get; set; }
        // Hoe besteld: Phone, Email, Portal of Other; optioneel.
        public string OrderChannel { get; set; }
        public string Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SupplyCommitmentLine> Lines { get; set; } = new List<SupplyCommitmentLine>();
    }
}
