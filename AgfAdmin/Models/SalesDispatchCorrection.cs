namespace AgfAdmin.Models
{
    // Administratieve correctie van een per abuis geregistreerde verzending: de goederen hebben het magazijn niet verlaten
    // (door de gebruiker bevestigd, niet door de software vastgesteld). De oorspronkelijke verzending, haar regels en
    // Dispatch-mutaties blijven onaangeroerd; per regel komt een nieuwe inhoudsregel in quarantaine op dezelfde pallet,
    // één compenserende Correction-mutatie en de reservering gaat Consumed -> Corrected (eindtoestand). Append-only.
    public sealed class SalesDispatchCorrection
    {
        public int Id { get; set; }

        public Guid RequestId { get; set; }

        // Hooguit één correctie per verzending (hele verzending).
        public int SalesDispatchId { get; set; }
        public SalesDispatch SalesDispatch { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Reason { get; set; }
        public string ActorReference { get; set; }

        // Expliciete bevestiging van de gebruiker; onderdeel van de audit en van de idempotentievergelijking.
        public bool GoodsNeverLeftWarehouseConfirmed { get; set; }

        public DateTime CorrectedAt { get; set; }

        public ICollection<SalesDispatchCorrectionLine> Lines { get; set; } = new List<SalesDispatchCorrectionLine>();
    }

    // Eén gecorrigeerde verzendregel: hoeveelheid, eenheid en kg zijn een exacte kopie van de verzendregel.
    // De herstelde inhoudsregel en de Correction-mutatie verwijzen naar deze regel (één gezaghebbende FK-richting,
    // dus geen cirkel bij het invoegen); hier alleen de omgekeerde navigaties.
    public sealed class SalesDispatchCorrectionLine
    {
        public int Id { get; set; }

        public int SalesDispatchCorrectionId { get; set; }
        public SalesDispatchCorrection SalesDispatchCorrection { get; set; }

        public int SalesDispatchLineId { get; set; }
        public SalesDispatchLine SalesDispatchLine { get; set; }

        public int StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }

        public int ExpectedReservationVersion { get; set; }

        // Omgekeerde navigaties (FK staat op de andere kant).
        public PalletContentLine RestoredContentLine { get; set; }
        public StockMovement StockMovement { get; set; }
    }
}
