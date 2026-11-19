namespace AgfAdmin.DTOs
{
    // Landenlijst voor de herkomst (api/countries): alle landen en de eigen aliassen (snelkoppelingen in de landkiezer).
    public sealed class CountryReferenceDto
    {
        public List<CountryDto> Countries { get; set; } = new();
        public List<CountryAliasDto> Aliases { get; set; } = new();
    }

    public sealed class CountryDto
    {
        public string Code { get; set; }
        public string NameNl { get; set; }
        public string NameEn { get; set; }
        // EU-lidstaat (VatRules.EuMemberStates): alleen daar geldt de btw-controle op een relatie (fixes 5b).
        public bool IsEu { get; set; }
    }

    public sealed class CountryAliasDto
    {
        public int Id { get; set; }
        public string Alias { get; set; }
        public string CountryCode { get; set; }
    }

    public sealed class AddCountryAliasDto
    {
        public string Alias { get; set; }
        public string CountryCode { get; set; }
    }
}
