namespace AgfAdmin.Services
{
    // De oude lezer voor de mailroute (EmailIngestionService) gaf nagebootste gegevens. Tot de mailbox (stap 7b) de echte lezer gebruikt,
    // weigert deze route: liever geen voorstel dan een verzonnen.
    public sealed class UnavailableAIExtractorService : IAIExtractorService
    {
        public Task<ExtractedInvoiceAuthData> ExtractDataAsync(string filePath) =>
            throw new InvalidOperationException("AI reading of e-mail attachments is not available yet; upload the invoice at Inkoopfactuur invoeren.");
    }
}
