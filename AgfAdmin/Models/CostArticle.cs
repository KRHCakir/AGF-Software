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

        // Een van de standaardsoorten uit de migratie (Handling, Transport, QC, Zeevracht, Afval, Overig).
        public bool IsStandard { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Weergavenaam van de ingelogde gebruiker (ICurrentUser), nooit van de client; "Standaard" bij de standaardsoorten.
        public string CreatedBy { get; set; }
    }
}
