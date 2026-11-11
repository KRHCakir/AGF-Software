namespace AgfAdmin.Models
{
    // Emballageregel op een verkooporder ("Pallet wegwerp", 1 stuk à 7,50): een emballage-artikel, een heel aantal en een prijs per stuk.
    // Doet niets met de voorraad: geen reservering, geen vrije voorraad, geen verzending. Daarom een eigen tabel naast SalesOrderLines.
    // Artikel, aantal, prijs en btw zijn wat de latere verkoopfactuur nodig heeft. Na opslaan onveranderlijk (guard-regel 27).
    public sealed class SalesOrderPackagingLine
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        // Altijd een kostenartikel van de soort Packaging.
        public int CostArticleId { get; set; }
        public CostArticle CostArticle { get; set; }

        public int Quantity { get; set; }          // heel aantal stuks, > 0
        public decimal? UnitPrice { get; set; }    // excl. btw, >= 0 (0,00 = gratis ruil); null = nog geen prijs
        public decimal VatPercentage { get; set; } // van het artikel bij opslaan

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
