namespace AgfAdmin.Models
{
    // Reservering van één orderregel op één bronregel. Source bepaalt of fysieke, verwachte of
    // direct-trade hoeveelheid wordt gereserveerd; een reservering maakt verwachte voorraad nooit fysiek.
    public sealed class StockReservation
    {
        public int Id { get; set; }

        // Legacy-eigenaar (nooit gebruikt door de nieuwe verkoopflow). Precies één eigenaar is verplicht (database-check).
        public int? OrderLineId { get; set; }
        public OrderLine OrderLine { get; set; }

        // Eigenaar in de nieuwe verkoopflow.
        public int? SalesOrderLineId { get; set; }
        public SalesOrderLine SalesOrderLine { get; set; }

        // Idempotentiesleutels van het aanmaak- en vrijgavecommando (uniek).
        public Guid? RequestId { get; set; }
        public Guid? ReleaseRequestId { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        // Optioneel: de fysieke inhoudsregel (partij op een pallet), zodra die bestaat.
        public int? PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }            // expliciete eenheid; optioneel tijdens de overgang
        public decimal? NetWeightKg { get; set; }   // werkelijk netto kg; nooit afgeleid
        public string Source { get; set; } // "Physical", "Expected" of "DirectTrade"
        public string Status { get; set; } // "Active", "Released" of "Consumed" (database-check)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReleasedAt { get; set; }
        public string ReleaseReason { get; set; }

        // Gezet bij Consumed (database-check); de verzending volgt via DispatchLine.
        public DateTime? ConsumedAt { get; set; }
        public SalesDispatchLine DispatchLine { get; set; }

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext bij elke wijziging (vrijgeven, afboeken).
        public int Version { get; set; }
    }
}
