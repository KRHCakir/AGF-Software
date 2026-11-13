using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using Microsoft.Extensions.Options;

namespace AgfAdmin.Configuration
{
    // Alle domeinservices van de API op één plek: Program.cs en de tests die de echte controllers op een lege database starten
    // (schone start) registreren zo precies dezelfde set. De database en CORS regelt de aanroeper.
    public static class ServiceRegistration
    {
        public static IServiceCollection AddAgfServices(this IServiceCollection services, string contentRootPath)
        {
            // AI & Email Services. De oude mailroute (7b) heeft nog geen lezer: nooit nepgegevens in de gewone werking.
            services.AddScoped<IEmailIngestionService, EmailIngestionService>();
            services.AddScoped<IAIExtractorService, UnavailableAIExtractorService>();
            // AI-lezen van inkoopfacturen (stap 7a): instellingen uit DocumentAi, documenten in DocumentAi:StoragePath.
            services.AddOptions<DocumentAiOptions>().BindConfiguration(DocumentAiOptions.Section);
            services.AddSingleton<IDocumentStore>(provider =>
            {
                var path = provider.GetRequiredService<IOptions<DocumentAiOptions>>().Value.StoragePath;
                return new FileDocumentStore(Path.IsPathRooted(path) ? path : Path.Combine(contentRootPath, path));
            });
            services.AddSingleton<IDocumentAiFactory, DocumentAiFactory>();
            services.AddSingleton<UploadRateLimiter>();
            services.AddScoped<InvoiceReader>();
            services.AddScoped<AiInvoiceReadingService>();
            services.AddScoped<AiInvoiceMatching>();
            services.AddHostedService<AiUploadCleanupService>();
            services.AddScoped<ISalesParserService, SimpleSalesParserService>();
            services.AddScoped<PurchaseInvoiceService>();
            services.AddScoped<ArticleService>();
            services.AddScoped<ArticleLifecycleService>();
            services.AddScoped<ArticleFromEntryService>();
            services.AddScoped<RelationService>();
            services.AddScoped<PaymentConditionService>();
            services.AddScoped<PurchaseOrderService>();
            services.AddScoped<LoadingSlipService>();
            services.AddScoped<SalesOrderTransportService>();
            services.AddScoped<DocumentRecipientService>();
            services.AddScoped<MailDraftService>();
            services.AddScoped<MailTemplateService>();
            // Mail versturen (documenten deel 3): Mail:Sender = Fake voor tests en de rondleiding (nooit netwerk), anders SMTP.
            services.AddSingleton<Services.Mail.IMailSender>(provider =>
                string.Equals(provider.GetRequiredService<IConfiguration>()["Mail:Sender"], Services.Mail.FakeMailSender.Name, StringComparison.OrdinalIgnoreCase)
                    ? new Services.Mail.FakeMailSender() : new Services.Mail.SmtpMailSender());
            services.AddDataProtection();
            services.AddScoped<Services.Mail.MailSettingsService>();
            services.AddSingleton<Services.Mail.MailSendLimiter>();
            services.AddScoped<Services.Mail.MailSendService>();
            services.AddScoped<PartijService>();
            services.AddScoped<CostArticleService>();
            services.AddScoped<PartijCostService>();
            // ECB-koersen voor facturen in een andere valuta; ExchangeRates:Source = Off zet ophalen uit (afgeschermde testomgeving).
            services.AddHttpClient<EcbExchangeRateSource>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/csv");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AGF-Software/1.0");
            });
            services.AddScoped<IExchangeRateSource>(provider =>
                string.Equals(provider.GetRequiredService<IConfiguration>()["ExchangeRates:Source"], "Off", StringComparison.OrdinalIgnoreCase)
                    ? new DisabledExchangeRateSource()
                    : provider.GetRequiredService<EcbExchangeRateSource>());
            services.AddScoped<InvoiceExchangeRateService>();
            services.AddScoped<ReceiptDeviationService>();
            services.AddScoped<CountryService>();
            services.AddSingleton<IEmailDomainChecker, DnsEmailDomainChecker>();
            // Lokale referentielijst van goederencodes: bij het opstarten gelezen uit Data/Reference/goods-codes.json.
            services.AddSingleton(_ => GoodsCodeCatalog.Load(Path.Combine(contentRootPath, GoodsCodeCatalog.DefaultRelativePath)));
            services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();

            // Beschikbaarheidsdomein
            services.AddScoped<ISupplyCommitmentService, SupplyCommitmentService>();
            services.AddScoped<IPalletService, PalletService>();
            services.AddScoped<IAvailabilityService, AvailabilityService>();
            services.AddScoped<ISalesOrderService, SalesOrderService>();
            services.AddScoped<IStockReservationService, StockReservationService>();
            services.AddScoped<ISalesDispatchService, SalesDispatchService>();
            services.AddScoped<ISalesDispatchCorrectionService, SalesDispatchCorrectionService>();
            services.AddScoped<IDirectTradeAllocationService, DirectTradeAllocationService>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IBusinessCalendar, AmsterdamBusinessCalendar>();
            services.AddScoped<IDirectTradeShipmentService, DirectTradeShipmentService>();
            services.AddScoped<IDirectTradeShipmentCorrectionService, DirectTradeShipmentCorrectionService>();
            services.AddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();
            services.AddScoped<IDeliveryRetractionService, DeliveryRetractionService>();
            services.AddScoped<ICustomerService, CustomerService>();
            return services;
        }
    }
}
