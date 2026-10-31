namespace AgfAdmin.Models
{
    // Land uit de vaste lijst (CountryList, ISO 3166-1 alpha-2). Herkomst van een artikel verwijst hiernaar; getoond wordt altijd de
    // Nederlandse naam. Alleen via de migratie (HasData) gevuld, nooit door de applicatie gewijzigd.
    public sealed class Country
    {
        public string Code { get; set; }
        public string NameNl { get; set; }
        public string NameEn { get; set; }
    }

    // Eigen snelkoppeling voor een land (bedrijfsbreed), bijv. "ZAF" of "Kaap" -> ZA. Alleen invoerhulp in de landkiezer: opgeslagen
    // en vergeleken wordt altijd het land. Een alias wijst naar precies één land (unieke sleutel) en is nooit de code of naam van een
    // ander land (CountryRules.AliasConflict).
    public sealed class CountryAlias
    {
        public int Id { get; set; }
        public string Alias { get; set; }
        // Vergelijkingssleutel: getrimd, kleine letters, zonder accenten (CountryRules.Key); uniek.
        public string AliasKey { get; set; }
        public string CountryCode { get; set; }
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
