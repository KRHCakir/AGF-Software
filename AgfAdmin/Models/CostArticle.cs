namespace AgfAdmin.Models
{
    // Kostenartikel (type Dienst): wat er op een factuur van derden staat, zoals Handling, Transport, QC, Zeevracht, Afval of Overig, of een
    // eigen soort (Elektra, Autohuur). Geen voorraad, geen maat, klasse of herkomst: daarom een eigen kleine tabel en geen ProductVariant,
    // zodat een Dienst nooit in Beschikbaarheid, ontvangst, verkoop of "Maat en klasse opschonen" terechtkomt. Het kostenartikel is de
    // soort van een kostenregel op een partij (P2). Niet verwijderen: een kostenartikel dat niet meer gebruikt wordt, gaat op inactief.
    public sealed class CostArticle
    {
        public int Id { get; set; }

        // Idempotentiesleutel van de client bij zelf aanmaken; null bij de standaardsoorten uit de migratie.
        public Guid? RequestId { get; set; }

        public string Name { get; set; }
        // Vergelijkingssleutel (CostArticleRules.NameKey): "Zee vracht " en "zee vracht" zijn één kostenartikel. Uniek.
        public string NameKey { get; set; }

        // Soort (CostArticleRules.Kinds): Cost = kosten op een factuur van derden (P2); Packaging = emballage (fust) zoals pallets en kratten,
        // ook als regel op een verkooporder. Verandert nooit (guard-regel 27).
        public string Kind { get; set; } = Services.CostArticleRules.KindCost;
        // Alleen bij emballage: optionele standaardprijs per stuk (excl. btw) en het btw-percentage voor een verkoopregel.
        public decimal? DefaultUnitPrice { get; set; }
        public decimal? VatPercentage { get; set; }

        // Een van de standaardsoorten uit de migraties (Handling, Transport, QC, Zeevracht, Afval, Overig; Pallet wegwerp, Euro-pallet, Krat).
        public bool IsStandard { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Weergavenaam van de ingelogde gebruiker (ICurrentUser), nooit van de client; "Standaard" bij de standaardsoorten.
        public string CreatedBy { get; set; }
    }
}
