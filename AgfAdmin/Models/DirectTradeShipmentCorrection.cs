namespace AgfAdmin.Models
{
    // Administratieve terugname van een volledige DirectTrade-verzending die niet heeft plaatsgevonden (door de gebruiker verklaard,
    // niet door de software vastgesteld). De oorspronkelijke verzending, regels en snapshots blijven ongewijzigd; per verzendregel
    // komt een correctieregel en de toewijzing gaat Consumed -> Corrected (eindtoestand). Geen voorraad, pallet, mutatie of retour.
    // Hooguit één correctie per verzending (unieke index). Append-only.
    public sealed class DirectTradeShipmentCorrection
    {
        public int Id { get; set; }

        public Guid RequestId { get; set; }

        public int DirectTradeShipmentId { get; set; }
        public DirectTradeShipment DirectTradeShipment { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Reason { get; set; }
        public string ActorReference { get; set; }

        // Expliciete verklaring van de gebruiker: vlag, taalonafhankelijke code en de vaste verklaringstekst (database-checks).
        public bool ShipmentDidNotTakePlaceConfirmed { get; set; }
        public string ConfirmationCode { get; set; }
        public string ConfirmationStatement { get; set; }

        // Echt UTC-tijdstip van vastleggen.
        public DateTime CorrectedAt { get; set; }

        public ICollection<DirectTradeShipmentCorrectionLine> Lines { get; set; } = new List<DirectTradeShipmentCorrectionLine>();
    }

    // Eén teruggenomen verzendregel: exacte kopie van hoeveelheid, eenheid, kg, bronregel en orderregel van de verzendregel.
    // De details per bron staan hier (niet in de audit), zodat de audit begrensd blijft.
    public sealed class DirectTradeShipmentCorrectionLine
    {
        public int Id { get; set; }

        public int DirectTradeShipmentCorrectionId { get; set; }
        public DirectTradeShipmentCorrection DirectTradeShipmentCorrection { get; set; }

        public int DirectTradeShipmentLineId { get; set; }
        public DirectTradeShipmentLine DirectTradeShipmentLine { get; set; }

        public int StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public int SalesOrderLineId { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }

        public int ExpectedReservationVersion { get; set; }
    }
}
