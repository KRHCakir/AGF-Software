namespace AgfAdmin.Models
{
    // Verzending van klantgebonden DirectTrade-aanvoer (leverancier/herkomst -> klant, zonder ons magazijn) voor één verkooporder.
    // Verbruikt hele Active DirectTrade-toewijzingen. Append-only. Geen voorraadmutatie, geen pallet, geen magazijnontvangst.
    // Verzonden betekent niet afgeleverd: het systeem weet alleen dat er (nog) geen afleverbevestiging is.
    // Klant is als snapshot vastgelegd naast de verwijzing: een latere stamgegevenswijziging verandert de historische verzending niet.
    public sealed class DirectTradeShipment
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel (uniek).
        public Guid RequestId { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public int CustomerId { get; set; }
        public Customer Customer { get; set; }
        public string CustomerNameSnapshot { get; set; }

        // Kalenderdatum zonder tijdstip; "vandaag" wordt op de backend in Europe/Amsterdam bepaald (nooit in de toekomst).
        public DateOnly ShippedOn { get; set; }
        public string Reference { get; set; }      // optioneel, bijvoorbeeld CMR- of vervoerdersreferentie
        public string ActorReference { get; set; }

        // Echt UTC-tijdstip van vastleggen.
        public DateTime RecordedAt { get; set; }

        public ICollection<DirectTradeShipmentLine> Lines { get; set; } = new List<DirectTradeShipmentLine>();

        // Omgekeerde navigatie: de (hooguit ene) terugname van deze verzending.
        public DirectTradeShipmentCorrection Correction { get; set; }

        // Omgekeerde navigatie: de (hooguit ene) afleverbevestiging van deze verzending.
        public ShipmentDeliveryConfirmation Delivery { get; set; }
    }

    // Eén verbruikte toewijzing met de werkelijk verzonden hoeveelheid, eenheid, netto kg en partij (expliciet ingevoerd, nooit afgeleid).
    // Leverancier, herkomst en bestemming staan als snapshot naast de verwijzingen naar bronregel en toezegging.
    public sealed class DirectTradeShipmentLine
    {
        public int Id { get; set; }

        public int DirectTradeShipmentId { get; set; }
        public DirectTradeShipment DirectTradeShipment { get; set; }

        // Een toewijzing wordt hooguit één keer verbruikt (unieke index).
        public int StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }

        public int SalesOrderLineId { get; set; }
        public SalesOrderLine SalesOrderLine { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }
        public int SupplyCommitmentId { get; set; }

        public int VendorId { get; set; }
        public string VendorNameSnapshot { get; set; }
        public string OriginSnapshot { get; set; }
        public string DestinationSnapshot { get; set; }

        // Eerste versie: alleen hele toewijzingen, dus Quantity == AllocatedQuantity (database-check).
        public decimal AllocatedQuantity { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }

        // Werkelijke partij (verplicht) en de partij van de bronregel op het moment van verzenden (kan leeg zijn).
        // Wijkt de werkelijke partij af van een ingevulde bronpartij, dan is een toelichting verplicht (database-check en service).
        public string LotNumber { get; set; }
        public string SourceLotNumber { get; set; }
        public string LotDeviationReason { get; set; }

        public int ExpectedReservationVersion { get; set; }
    }
}
