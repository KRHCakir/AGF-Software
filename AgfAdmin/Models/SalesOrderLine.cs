namespace AgfAdmin.Models
{
    // Klantvraag per variant + eenheid (uniek per order). Reserveringen hangen aan deze regel.
    public sealed class SalesOrderLine
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public decimal Quantity { get; set; } // > 0, decimal(18,3)
        public string Unit { get; set; }      // box, colli, tray, piece of dozen; nooit omgerekend
        // Oorspronkelijke palletinvoer (optioneel, beide of geen): Quantity = PalletCount × PackagesPerPallet + LoosePackages.
        // Quantity blijft de bron voor vraag, reservering en verzending; resterend wordt nooit naar hele pallets afgerond.
        // Nieuwe invoer heeft hele pallets; oudere regels kunnen een gebroken palletaantal hebben (blijft leesbaar).
        public decimal? PalletCount { get; set; }
        public decimal? PackagesPerPallet { get; set; }
        // Losse verpakkingen naast de hele pallets ("3 pallets × 120 + 15 colli"); alleen bij palletinvoer, heel en >= 0.
        public decimal? LoosePackages { get; set; }
        // Verwachte netto en bruto kg van de regel (optioneel; het gewogen gewicht volgt bij levering). Bruto nooit onder netto.
        // WeightSource: null zonder kg, Calculated = aantal × artikelstandaard, Entered = door de gebruiker opgegeven (LineWeightRules).
        public decimal? NetWeightKg { get; set; }
        public decimal? GrossWeightKg { get; set; }
        public string WeightSource { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token; wordt bij elke reservering "aangeraakt".
        public int Version { get; set; }

        public ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();
    }
}
