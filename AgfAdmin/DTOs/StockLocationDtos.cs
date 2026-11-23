namespace AgfAdmin.DTOs
{
    // Voorraadlocaties (feedback 22 november): Instellingen › Voorraadlocaties. De eigen loods is geen rij (null = eigen loods).
    public sealed class StockLocationDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Address { get; set; }
        public string CountryCode { get; set; }
        public bool IsActive { get; set; }
        // Er wijst een pallet of inkooporder naar deze locatie: de soort ligt dan vast (guard-regel 40).
        public bool InUse { get; set; }
        public int Version { get; set; }
    }

    public sealed class SaveStockLocationDto
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Address { get; set; }
        public string CountryCode { get; set; }
        public bool? IsActive { get; set; }
        // Bij aanpassen: de versie zoals getoond (gelijktijdig aanpassen geeft een conflict in plaats van stil overschrijven).
        public int? ExpectedVersion { get; set; }
    }
}
