namespace AgfAdmin.DTOs
{
    // Artikelwerkplek (api/articles): één artikel = één ProductVariant met haar Product (productsoort). Geen nieuwe catalogus;
    // dezelfde rijen als api/products?includeVariants=true, maar met duidelijke labels en alle optionele stamgegevens.
    public sealed class ArticleDto
    {
        public int Id { get; set; }                 // ProductVariant.Id
        public int ProductId { get; set; }
        public string ProductName { get; set; }     // productsoort / productnaam
        public string VariantName { get; set; }     // ras / variëteit (mag leeg zijn)
        public string Description { get; set; }     // herkenbare artikelomschrijving (afgeleid)
        public string ShortcutCode { get; set; }
        public string Origin { get; set; }
        public string Brand { get; set; }
        public string SizeCategory { get; set; }
        public string Packaging { get; set; }       // vrije verpakkingsomschrijving ("4kg Box")
        public string PackagingUnit { get; set; }   // box, colli, tray, piece, dozen
        public string Season { get; set; }
        public string Notes { get; set; }
        public string WeightMethod { get; set; }
        public decimal? DefaultNetKgPerPackage { get; set; }
        public decimal? DefaultGrossKgPerPackage { get; set; }
        public decimal? DefaultPackagesPerPallet { get; set; }
        public string GoodsCode { get; set; }
        public string GoodsCodeSystem { get; set; }
        public string TradeClass { get; set; }
        // Standaard-GGN (GLOBALG.A.P.-nummer, 13 cijfers als tekst): invoerhulp bij ontvangst, zoals opgegeven; certificeringsstatus niet gecontroleerd.
        public string DefaultGgn { get; set; }
        public decimal DefaultPrice { get; set; }
    }

    // POST api/articles en PUT api/articles/{id}. Lege tekst = niet vastgelegd (null); alleen productsoort is verplicht.
    public sealed class SaveArticleDto
    {
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public string ShortcutCode { get; set; }
        public string Origin { get; set; }
        public string Brand { get; set; }
        public string SizeCategory { get; set; }
        public string Packaging { get; set; }
        public string PackagingUnit { get; set; }
        public string Season { get; set; }
        public string Notes { get; set; }
        public string WeightMethod { get; set; }
        public decimal? DefaultNetKgPerPackage { get; set; }
        public decimal? DefaultGrossKgPerPackage { get; set; }
        public decimal? DefaultPackagesPerPallet { get; set; }
        public string GoodsCode { get; set; }
        public string GoodsCodeSystem { get; set; }
        public string TradeClass { get; set; }
        public string DefaultGgn { get; set; }
    }

    // Keuzelijsten en bestaande productsoorten voor het formulier.
    public sealed class ArticleReferenceDto
    {
        public List<string> ProductNames { get; set; } = new();
        public List<string> PackagingUnits { get; set; } = new();
        public List<string> WeightMethods { get; set; } = new();
        public List<string> GoodsCodeSystems { get; set; } = new();
        public List<string> TradeClassSuggestions { get; set; } = new();
    }

    // Lokale referentielijst voor goederencodes: herkomst, versie en geldigheid zijn altijd zichtbaar; de lijst is nooit
    // volledig en de gebruiker kiest en bevestigt zelf.
    public sealed class GoodsCodeSearchDto
    {
        // Het gezochte codestelsel (HS, CN of TARIC); Matches en TotalEntries gelden uitsluitend voor dit stelsel.
        public string System { get; set; }
        public GoodsCodeSourceDto Source { get; set; }
        public bool Complete { get; set; }
        public int TotalEntries { get; set; }
        public List<GoodsCodeMatchDto> Matches { get; set; } = new();
    }

    public sealed class GoodsCodeSourceDto
    {
        public string System { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Version { get; set; }
        public string ValidFrom { get; set; }
        public string RetrievedAt { get; set; }
        public string Note { get; set; }
        public string LookupUrl { get; set; }
    }

    public sealed class GoodsCodeMatchDto
    {
        public string Code { get; set; }
        public string System { get; set; }
        public string HsCode { get; set; }
        public string DescriptionNl { get; set; }
        public string DescriptionEn { get; set; }
        // Alleen bij HS: de GN-codes uit de lijst die tot deze post zijn samengevoegd (leeg als de post geen GN-codes in de lijst heeft).
        public List<string> MergedCodes { get; set; }
    }
}
