namespace AgfAdmin.Models
{
    // Blijvende, oplopende versie van de ontvangerslijst van één eigenaar (relatie, of de loods met OwnerId 0). Wordt bij elke opslag met
    // één verhoogd, ook als de lijst daarna leeg is, zodat een oude versie nooit opnieuw geldig wordt. Geen regel = versie 0.
    public sealed class DocumentRecipientListVersion
    {
        public string OwnerKind { get; set; }   // Relation of Warehouse
        public int OwnerId { get; set; }        // relatie-id; 0 voor de loods
        public long Version { get; set; }
    }
}
