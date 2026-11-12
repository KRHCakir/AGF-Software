namespace AgfAdmin.Models
{
    // Transport van een verkooporder (documenten deel 2): wie rijdt (een vervoerder, of eigen transport / klant haalt op), waar geladen en
    // waar gelost wordt, met de gegevens voor de transportopdracht. Hooguit één per order; alleen als er transport is. Laden op (datum en
    // tijd) staat op de order zelf (SalesOrder.LoadAt), dezelfde als op de laadbon. Prijzen of tarieven horen hier nooit bij.
    public sealed class SalesOrderTransport
    {
        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Mode { get; set; }                 // Carrier of Own (eigen transport / klant haalt op)
        public int? CarrierRelationId { get; set; }      // alleen bij Carrier: een relatie met de rol vervoerder
        public Relation CarrierRelation { get; set; }

        public string LoadingPlace { get; set; }         // naam en adres; leeg = de loods uit de instellingen (anders het bedrijfsadres)
        public int? UnloadingAddressId { get; set; }     // een adres van de klant; leeg = het standaard afleveradres
        public RelationAddress UnloadingAddress { get; set; }
        public string UnloadingPlace { get; set; }       // afwijkend adres (vrije tekst); gaat voor het gekozen adres

        public DateTime? UnloadDate { get; set; }        // verwachte losdatum (alleen de datum)
        public decimal? TemperatureC { get; set; }       // °C, optioneel
        public string LoadingReference { get; set; }     // referentie van de loods
        public string UnloadingReference { get; set; }   // leeg = de klantreferentie van de order
        public string Remarks { get; set; }
        public string PalletKind { get; set; }           // leeg = de standaard van het bedrijf ("Europallet")

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }
}
