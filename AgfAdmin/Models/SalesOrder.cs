namespace AgfAdmin.Models
{
    // Schone verkooporder voor het nieuwe beschikbaarheidsdomein. Bewust los van de legacy Order/OrderLine
    // (int zonder eenheid, harde delete, legacy Inventory). Geen prijzen, btw of facturen in deze fase.
    public sealed class SalesOrder
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel.
        public Guid RequestId { get; set; }

        public int CustomerId { get; set; }
        public Customer Customer { get; set; }

        public string Status { get; set; }               // Open, Cancelled

        // Eigen verkoopordernummer "VO-2026-0001": doorlopend per jaar (Europe/Amsterdam) en uniek, bij aanmaak toegekend (zoals IO-nummers).
        // OrderYear + OrderSequence zijn de bron (uniek samen); OrderNumber is de vaste weergave daarvan en verandert nooit (guard-regel 22).
        public string OrderNumber { get; set; }
        public int OrderYear { get; set; }
        public int OrderSequence { get; set; }
        // Verkoop-partij (P2): het partijnummer waarmee op papier gewerkt wordt (261140), naast het VO-nummer. Bij het eerste opslaan
        // toegekend (voorgesteld of zelf getypt), daarna 1-op-1 met deze order. Welk van de twee later het hoofdnummer wordt, kiest de eigenaar.
        public Partij SalesPartij { get; set; }
        // Partij uit de oorspronkelijke aanvraag, voor de replayvergelijking: "auto" (voorgesteld) of "new:<sleutel>" (zelf getypt).
        // Null bij orders van vóór de verkoop-partij.
        public string RequestPartijKey { get; set; }
        public string CustomerReference { get; set; }    // ordernummer van de klant; optioneel, getrimd, leeg = null; doorzoekbaar
        // Interne opmerking bij deze order: alleen voor interne werkplekken, nooit in klantdocumenten, mails of exports;
        // telt mee in de replayvergelijking en wordt niet naar volgende orders overgenomen.
        public string InternalNote { get; set; }
        public DateTime RequestedDeliveryDate { get; set; } // UTC, afgekapt op microseconden
        public string ActorReference { get; set; }

        // Annulering: alleen gevuld bij Status Cancelled (database-check). De reden is genormaliseerd (getrimd, max 500).
        public DateTime? CancelledAt { get; set; }
        public string CancelReason { get; set; }
        // Idempotentiesleutel van het annuleringscommando (uniek); de vrijgaven die eruit volgen dragen deze sleutel in hun event-snapshot.
        public Guid? CancelRequestId { get; set; }

        // Bevestigd (documenten deel 1): wanneer en door wie (CurrentUser); daarna maakt het systeem de laadbon (SalesOrderDocument). Vast
        // zodra gezet (TraceabilityGuard regel 31). Laden op en kenteken staan op de laadbon en mogen daarna nog veranderen (nieuwe versie).
        public DateTime? ConfirmedAt { get; set; }
        public string ConfirmedBy { get; set; }
        public DateTime? LoadAt { get; set; }            // UTC, datum en tijd van laden; leeg = de leverdatum zonder tijd
        public string LicensePlate { get; set; }         // kenteken; leeg = met de hand invullen op de bon

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }

        public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
        // Emballage (pallets, kratten) naast de goederen; doet niets met de voorraad (zie SalesOrderPackagingLine).
        public ICollection<SalesOrderPackagingLine> PackagingLines { get; set; } = new List<SalesOrderPackagingLine>();
        // Documenten bij deze order (laadbon, later transportopdracht), elke versie apart en nooit gewijzigd.
        public ICollection<SalesOrderDocument> Documents { get; set; } = new List<SalesOrderDocument>();
    }
}
