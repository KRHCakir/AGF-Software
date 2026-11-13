namespace AgfAdmin.Models
{
    // Een e-mailadres dat documenten krijgt (documenten deel 3): van een relatie (klant, vervoerder), eventueel alleen voor één afleveradres
    // van die relatie, of van de loods (OwnerKind Warehouse, geen relatie). Per adres de documentsoorten (vlaggen, zie DocumentMailRules)
    // en Aan of Cc. Zonder ontvangers voor een afleveradres gelden die van de relatie.
    public sealed class DocumentRecipient
    {
        public int Id { get; set; }
        public string OwnerKind { get; set; }          // Relation of Warehouse
        public int? RelationId { get; set; }
        public Relation Relation { get; set; }
        public int? RelationAddressId { get; set; }    // alleen voor dit afleveradres; leeg = de hele relatie
        public RelationAddress RelationAddress { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int Kinds { get; set; }                 // vlaggen: orderbevestiging, laadbon, transportopdracht, factuur, overig
        public string Role { get; set; }               // To of Cc
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }
    }
}
