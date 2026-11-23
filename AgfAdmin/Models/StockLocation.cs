namespace AgfAdmin.Models
{
    // Feedback 22 november: een voorraadlocatie met een soort. Voorraad hoeft niet in de eigen loods te liggen: ook bij de leverancier, op een
    // cross-dock, in een externe opslag of onderweg (direct naar een klant in een ander land). De eigen loods zelf is geen rij: een pallet of
    // order zonder locatie ligt in de eigen loods (het adres uit de instellingen), zo blijven alle bestaande pallets en orders Eigen loods
    // zonder dat er een rij verandert. Een locatie wordt nooit verwijderd (inactief zetten), en haar soort ligt vast zodra er een pallet of
    // order naar wijst (guard-regel 40).
    public sealed class StockLocation
    {
        public int Id { get; set; }
        public string Name { get; set; }              // bijv. "Onderweg naar Griekenland", "Cross-dock Venlo"
        public string Kind { get; set; }              // OwnWarehouse, Supplier, Crossdock, ExternalStorage, InTransit (StockLocationRules)
        public string Address { get; set; }           // naam en adres zoals op de transportopdracht (vrije tekst, meerdere regels)
        public string CountryCode { get; set; }       // ISO 3166-1 alfa-2, optioneel (Onderweg naar …)
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }
}
