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
        // Verkoopfactuur 1: eigenaar bij een productregel van een directe verkoopfactuur (zonder order). Actief zolang de factuur een concept
        // is, verbruikt bij Definitief (SalesInvoiceStockLine), vrijgegeven bij weggooien of een gewijzigde regel. Altijd Physical (regel 46).
        public int? InvoiceLineId { get; set; }
        public InvoiceLine InvoiceLine { get; set; }
        // Feedback 22 november: de levering (en dus de verkoop-partij) waar deze reservering mee gaat; leeg = de eerste levering van de order.
        // Alleen bij een actieve reservering te verplaatsen, met een auditregel (regel 42).
        public int? SalesDeliveryId { get; set; }
        public SalesDelivery SalesDelivery { get; set; }
        public SalesOrderLine SalesOrderLine { get; set; }

        // Idempotentiesleutels van het aanmaak- en vrijgavecommando (uniek).
        public Guid? RequestId { get; set; }
        public Guid? ReleaseRequestId { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        // De fysieke inhoudsregel (partij op een pallet): verplicht bij Source Physical, altijd leeg bij Source DirectTrade
        // (een toewijzing van verwachte, klantgebonden aanvoer staat nooit op een pallet). Database-check.
        public int? PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }            // expliciete eenheid; optioneel tijdens de overgang
        public decimal? NetWeightKg { get; set; }   // werkelijk netto kg; nooit afgeleid; altijd leeg bij DirectTrade (geen gemeten kg)
        public string Source { get; set; } // "Physical" of "DirectTrade" (verkoopflow; database-check); legacy-rijen kunnen anders zijn
        public string Status { get; set; } // "Active", "Released" of "Consumed" (database-check)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReleasedAt { get; set; }
        public string ReleaseReason { get; set; }

        // Gezet bij Consumed (database-check); de verzending volgt via DispatchLine.
        public DateTime? ConsumedAt { get; set; }
        public SalesDispatchLine DispatchLine { get; set; }
        // Alleen bij een verbruikte DirectTrade-toewijzing: de DirectTrade-verzendregel (nooit een magazijnverzendregel).
        public DirectTradeShipmentLine DirectTradeShipmentLine { get; set; }
        // Alleen bij een teruggenomen DirectTrade-verzending (Corrected): de DirectTrade-correctieregel (nooit een magazijncorrectieregel).
        public DirectTradeShipmentCorrectionLine DirectTradeCorrectionLine { get; set; }

        // Gezet bij Corrected (eindtoestand na een verzendcorrectie; database-check). Telt niet als gereserveerd of verzonden.
        public DateTime? CorrectedAt { get; set; }

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext bij elke wijziging (vrijgeven, afboeken).
        public int Version { get; set; }
    }
}
