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
        public string Status { get; set; }      // "Expected", "PartiallyReceived", "Received", "Cancelled", "Delivered"
        public string Origin { get; set; }
        public string Destination { get; set; } // fysieke bestemming; verplicht voor DirectTrade (afgedwongen in servicecode)
        public DateTime? ExpectedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SupplyCommitmentLine> Lines { get; set; } = new List<SupplyCommitmentLine>();
    }
}
