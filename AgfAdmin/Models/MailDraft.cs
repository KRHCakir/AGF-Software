namespace AgfAdmin.Models
{
    // Een mailconcept bij één documentversie (documenten deel 3): ontstaat zodra het document er is, met de ontvangers voor die soort, een
    // onderwerp en tekst uit het sjabloon en de PDF als bijlage. Verstuurd wordt het alleen als iemand op Versturen klikt; dan legt het vast
    // wanneer, door wie, naar wie en met welk bericht-id (TraceabilityGuard regel 32: daarna verandert of verdwijnt het nooit).
    public sealed class MailDraft
    {
        public int Id { get; set; }
        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }
        public int DocumentId { get; set; }
        public SalesOrderDocument Document { get; set; }
        public string DocumentKind { get; set; }
        public string Status { get; set; }             // Draft, Sending, Sent, Failed, Discarded, Superseded
        public string To { get; set; }                 // adressen gescheiden door komma's
        public string Cc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string MissingRecipient { get; set; }   // naam van de relatie of "loods" zonder adres voor deze soort; leeg = er is een ontvanger
        public int? MissingRelationId { get; set; }
        public DateTime? PreviousSentAt { get; set; }  // een eerdere versie van dit document is al verstuurd op
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }
        public DateTime? SentAt { get; set; }
        public string SentBy { get; set; }
        public string MessageId { get; set; }
        public string LastError { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public string LastAttemptBy { get; set; }

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }

    // Afzender via SMTP (één rij). Het wachtwoord staat versleuteld (ASP.NET Data Protection), nooit leesbaar terug en nooit in logs.
    public sealed class MailSettings
    {
        public int Id { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 587;
        public string Security { get; set; } = Services.DocumentMailRules.SecurityStartTls;
        public string UserName { get; set; }
        public string ProtectedPassword { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
        public string Signature { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }
    }

    // Een eigen sjabloon per documentsoort (onderwerp en tekst met invulvelden); zonder rij geldt het vaste standaardsjabloon.
    public sealed class MailTemplate
    {
        public string Kind { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }
    }
}
