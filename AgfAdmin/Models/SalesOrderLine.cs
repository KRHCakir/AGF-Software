namespace AgfAdmin.Models
{
    // Klantvraag per variant + eenheid + bron (uniek per order). Reserveringen hangen aan deze regel.
    public sealed class SalesOrderLine
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public decimal Quantity { get; set; } // > 0, decimal(18,3)
        // Feedback 22 november: de gevraagde inkoop-partij (optioneel). Leeg = bij het reserveren de oudste partij eerst (FIFO), zoals altijd;
        // ingevuld = reserveren (FIFO) alleen uit deze partij. Een regel kan daarnaast via "Partij kiezen" uit meer partijen gereserveerd
        // worden. Alleen een inkoop-partij; vast na aanmaken (regel 43).
        public int? PartijId { get; set; }
        public Partij Partij { get; set; }
        public string Unit { get; set; }      // box, colli, tray, piece of dozen; nooit omgerekend
        // Bron: Stock (uit voorraad) of Expected (uit verwachte aanvoer, verkocht vóór aankomst); zie SalesSupplyRules. Oudere regels: Stock.
        // Per order één regel per variant + eenheid + bron, zodat een regel gesplitst kan worden (deel uit voorraad, rest verwacht).
        public string SupplySource { get; set; } = Services.SalesSupplyRules.SourceStock;
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

        // Feedback 22 november: prijs per eenheid van de regel (excl. btw, null = nog geen prijs), korting in procenten (null = geen) en
        // het btw-tarief (voorstel uit het artikel; null bij regels van vóór deze ronde). Bedrag en btw volgen uit OrderLineMath; na
        // opslaan liggen ze vast (guard-regel 36).
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public decimal? VatPercentage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token; wordt bij elke reservering "aangeraakt".
        public int Version { get; set; }

        public ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();
    }
}
