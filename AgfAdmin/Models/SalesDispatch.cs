namespace AgfAdmin.Models
{
    // Verzending (afboeking) voor één verkooporder: verbruikt gekozen Active reserveringen in hun geheel.
    // Append-only: een verzending wordt nooit gewijzigd of verwijderd; correcties volgen later als compenserende flow.
    public sealed class SalesDispatch
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel (uniek).
        public Guid RequestId { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Reference { get; set; }      // optioneel, bijvoorbeeld vervoerder- of documentreferentie
        public string ActorReference { get; set; }

        public DateTime DispatchedAt { get; set; }

        public ICollection<SalesDispatchLine> Lines { get; set; } = new List<SalesDispatchLine>();

        // Omgekeerde navigatie: alle afleverbevestigingen (historie), waarvan hooguit één actief is (DeliveryState).
        public ICollection<ShipmentDeliveryConfirmation> DeliveryConfirmations { get; set; } = new List<ShipmentDeliveryConfirmation>();
    }

    // Eén verbruikte reservering: hoeveelheid, eenheid en netto-kg zijn een kopie van de reservering (nooit afgeleid).
    public sealed class SalesDispatchLine
    {
        public int Id { get; set; }

        public int SalesDispatchId { get; set; }
        public SalesDispatch SalesDispatch { get; set; }

        // Een reservering wordt hooguit één keer verbruikt (unieke index).
        public int StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }

        public int SalesOrderLineId { get; set; }
        public SalesOrderLine SalesOrderLine { get; set; }

        public int PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }

        // De reserveringsversie waarop de beslissing was gebaseerd (voor de replay-vergelijking).
        public int ExpectedReservationVersion { get; set; }

        // De bijbehorende Dispatch-voorraadmutatie (het grootboek).
        public int StockMovementId { get; set; }
        public StockMovement StockMovement { get; set; }
    }
}
