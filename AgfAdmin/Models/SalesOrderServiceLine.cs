namespace AgfAdmin.Models
{
    // Dienstregel op een verkooporder (feedback 22 november): handling of transport die aan de klant wordt doorberekend, met een Dienst-artikel
    // (kostenartikel van de soort Cost), aantal, prijs, korting en het btw-tarief van het artikel (standaard 21%). Iets anders dan de handling
    // die het bedrijf zelf betaalt (die hangt als kost aan de partij). Doet niets met voorraad, pallets of kg en staat niet op de laadbon of de
    // transportopdracht; daarom een eigen tabel naast de goederen- en emballageregels. Na opslaan onveranderlijk (guard-regel 38).
    public sealed class SalesOrderServiceLine
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        // Altijd een kostenartikel van de soort Cost ("Dienst").
        public int CostArticleId { get; set; }
        public CostArticle CostArticle { get; set; }

        public decimal Quantity { get; set; }            // > 0, hooguit 3 decimalen
        public decimal? UnitPrice { get; set; }          // excl. btw, >= 0; null = nog geen prijs
        public decimal? DiscountPercentage { get; set; } // 0 tot en met 100; null = geen korting
        public decimal VatPercentage { get; set; }       // 0, 9 of 21; voorstel uit het artikel

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
