namespace AgfAdmin.Services.DocumentAi
{
    // Instellingen voor AI-lezen (sectie DocumentAi in appsettings.json, te overschrijven met omgevingsvariabelen DocumentAi__...).
    // De sleutel staat nooit in de repo: DocumentAi:ApiKey uit de gebruikersgeheimen of de omgeving, anders ANTHROPIC_API_KEY.
    // Model, vangnet en prijzen zijn instellingen: wisselen van model is een configuratiewijziging, geen codewijziging.
    public sealed class DocumentAiOptions
    {
        public const string Section = "DocumentAi";
        public const string ReaderClaude = "Claude";
        // Vaste, voorspelbare uitkomst zonder netwerk: alleen voor tests en de rondleiding, nooit in de gewone werking.
        public const string ReaderDemo = "Demo";

        public string Reader { get; set; } = ReaderClaude;
        public string ApiKey { get; set; }
        public string PrimaryModel { get; set; } = "claude-haiku-5-5";
        // Alleen Haiku (besluit eigenaar, 12 november): geen vangnet en geen ander model. Een tweede leesbeurt met een ander model kan
        // alleen nog als iemand die bewust configureert (FallbackEnabled en FallbackModel met een prijs onder Models).
        public bool FallbackEnabled { get; set; }
        public string FallbackModel { get; set; }
        // Prijs per model, gecontroleerd bij de aanbieder (CheckedOn); per leesactie vastgelegd. Zonder prijs leest het systeem niet.
        public Dictionary<string, DocumentAiModelPrice> Models { get; set; } = new();
        // Kosten in euro uit het echte verbruik in dollars, met deze koers (vastgelegd per lezing).
        public decimal EurPerUsd { get; set; } = 0.86m;
        public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
        public int MaxUploadsPerMinute { get; set; } = 6;
        public int TimeoutSeconds { get; set; } = 90;
        // Map voor de documenten (relatief aan de map van de API, of absoluut). Zie docs/ai-factuur.md over back-ups en versleuteling.
        public string StoragePath { get; set; } = "App_Data/documents";
        // Afgewezen, handmatig verder of mislukt: het bestand wordt na zoveel dagen verwijderd (goedgekeurde bijlagen nooit).
        public int RetentionDays { get; set; } = 30;
    }

    public sealed class DocumentAiModelPrice
    {
        public string DisplayName { get; set; }
        public decimal InputPerMillionUsd { get; set; }
        public decimal OutputPerMillionUsd { get; set; }
        // Staffel: boven zoveel invoertokens in één verzoek gelden de hogere prijzen voor het hele verzoek (Haiku 5.5: 100.000).
        public long? TierAboveInputTokens { get; set; }
        public decimal? TierInputPerMillionUsd { get; set; }
        public decimal? TierOutputPerMillionUsd { get; set; }
        public string CheckedOn { get; set; }
    }
}
