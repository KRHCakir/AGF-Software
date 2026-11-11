namespace AgfAdmin.Models
{
    // Eén AI-lezing van een geüpload document (stap 7a, inkoopfactuur): het bestand (naam, grootte, hash, opslagsleutel), welk model het
    // voorstel las (met de versie die de aanbieder terugmeldt), het voorstel zelf, de controles, het verbruik en de kosten. De AI slaat
    // nooit zelf iets op: pas wanneer een gebruiker het voorstel goedkeurt, hangt de lezing aan een inkoopfactuur (met wie en wanneer, de
    // goedgekeurde waarden en wat de gebruiker aanpaste). Afgewezen, handmatig verder of mislukt: het bestand wordt na de bewaartermijn
    // opgeruimd (FileDeletedAt), de regel blijft voor het verbruik per maand.
    public sealed class AiExtraction
    {
        public int Id { get; set; }
        public string Status { get; set; }          // Proposed, Approved, Rejected, Manual, Failed (AiReadingRules)
        public string DocumentType { get; set; }    // PurchaseInvoice

        public string FileName { get; set; }
        public string MediaType { get; set; }       // van het origineel (application/pdf, image/jpeg, image/png, image/heic)
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }          // van het origineel, kleine letters hex
        public string StorageKey { get; set; }
        // Weergave en AI-invoer als die anders is dan het origineel (een HEIC-foto als JPEG); anders leeg.
        public string DisplayStorageKey { get; set; }
        public string DisplayMediaType { get; set; }
        public DateTime? FileDeletedAt { get; set; }

        public string Provider { get; set; }        // Anthropic, of Demo (alleen tests en de rondleiding)
        public string Model { get; set; }           // het model dat het getoonde voorstel las; leeg als het lezen mislukte
        public string ModelVersion { get; set; }    // zoals de aanbieder het terugmeldt
        public bool UsedFallback { get; set; }      // het vangnetmodel las het voorstel
        // Elke poging (ook een herhaling bij een tijdelijke fout en het vangnet): model, versie, tokens, prijs, kosten, tijd, uitkomst.
        public string AttemptsJson { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public decimal CostUsd { get; set; }
        public decimal EurPerUsd { get; set; }
        public decimal CostEur { get; set; }
        public int DurationMs { get; set; }

        public string ProposalJson { get; set; }    // het gecontroleerde antwoord van het model (vast schema)
        public string ProblemsJson { get; set; }    // de controles: [{field, code}]
        public string ErrorCode { get; set; }       // bij Failed: unavailable, invalid, unsupported, failed

        public string ApprovedValuesJson { get; set; }
        public string AdjustmentsJson { get; set; } // [{field, proposed, final}]: wat de gebruiker aanpaste vóór goedkeuren
        public string ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int? IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }

        public string ClosedBy { get; set; }        // afgewezen of handmatig verder
        public DateTime? ClosedAt { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Een bijlage bij een inkoopfactuur (het originele document van een goedgekeurde AI-lezing). Append-only: nooit gewijzigd of
    // verwijderd (TraceabilityGuard regel 28); de hash maakt achteraf zichtbaar dat het bestand niet veranderd is.
    public sealed class DocumentAttachment
    {
        public int Id { get; set; }
        public int IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }
        public string FileName { get; set; }
        public string MediaType { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string StorageKey { get; set; }
        public string Source { get; set; }          // AiUpload
        public int? AiExtractionId { get; set; }
        public AiExtraction AiExtraction { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
