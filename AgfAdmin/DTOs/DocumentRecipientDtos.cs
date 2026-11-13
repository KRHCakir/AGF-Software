namespace AgfAdmin.DTOs
{
    // Documentontvangers van een relatie of van de loods (documenten deel 3). Kinds: OrderConfirmation, LoadingSlip, TransportOrder, Invoice,
    // Other. RelationAddressId leeg = de hele relatie; anders alleen voor dat afleveradres (zonder ontvangers daar gelden die van de relatie).
    public sealed class DocumentRecipientDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public List<string> Kinds { get; set; } = new();
        public int? RelationAddressId { get; set; }
    }

    public sealed class RecipientAddressOptionDto
    {
        public int Id { get; set; }
        public string Purpose { get; set; }
        public string Text { get; set; }
    }

    public sealed class DocumentRecipientsDto
    {
        // Vingerafdruk van de opgeslagen lijst: meesturen bij opslaan, zodat een gelijktijdige wijziging niet stil verloren gaat.
        public string Version { get; set; }
        public List<DocumentRecipientDto> Recipients { get; set; } = new();
        public List<RecipientAddressOptionDto> Addresses { get; set; } = new();
        public List<RelationWarningDto> Warnings { get; set; } = new();
    }

    public sealed class SaveDocumentRecipientsDto
    {
        public string ExpectedVersion { get; set; }
        public List<DocumentRecipientDto> Recipients { get; set; } = new();
    }
}
