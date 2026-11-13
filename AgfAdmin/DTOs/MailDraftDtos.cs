namespace AgfAdmin.DTOs
{
    // Een mailconcept bij een documentversie (documenten deel 3). To en Cc: adressen gescheiden door komma's.
    public sealed class MailDraftDto
    {
        public int Id { get; set; }
        public int SalesOrderId { get; set; }
        public string OrderNumber { get; set; }
        public string CustomerName { get; set; }
        public int DocumentId { get; set; }
        public string DocumentKind { get; set; }
        public string DocumentNumber { get; set; }
        public int DocumentVersion { get; set; }
        public string Status { get; set; }
        public string To { get; set; }
        public string Cc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        // Geen e-mailadres voor deze soort: de naam van de relatie (of "de loods") en haar id, om er een toe te voegen. Versturen kan dan niet.
        public string MissingRecipient { get; set; }
        public int? MissingRelationId { get; set; }
        // Een eerdere versie van dit document is al verstuurd of doorgegeven op.
        public DateTime? PreviousSentAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string SentBy { get; set; }
        public string MessageId { get; set; }
        public string LastError { get; set; }
        public int Attempts { get; set; }
        public int Version { get; set; }
    }

    public sealed class UpdateMailDraftDto
    {
        public int? ExpectedVersion { get; set; }
        public string To { get; set; }
        public string Cc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    public sealed class MailDraftActionDto
    {
        public int? ExpectedVersion { get; set; }
        // Meer dan 10 ontvangers: alleen versturen na een uitdrukkelijke bevestiging.
        public bool ConfirmManyRecipients { get; set; }
    }

    // Een sjabloon per documentsoort (Subject en Body met invulvelden); IsDefault = het vaste standaardsjabloon (geen eigen versie).
    public sealed class MailTemplateDto
    {
        public string Kind { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public bool IsDefault { get; set; }
    }

    public sealed class MailTemplatesDto
    {
        public List<MailTemplateDto> Templates { get; set; } = new();
        public List<string> Placeholders { get; set; } = new();
    }
}
