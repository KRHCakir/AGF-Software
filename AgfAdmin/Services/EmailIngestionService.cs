using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public class EmailIngestionService : IEmailIngestionService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailIngestionService> _logger;

        public EmailIngestionService(IServiceScopeFactory scopeFactory, ILogger<EmailIngestionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task CheckForNewEmailsAsync()
        {
            _logger.LogInformation("Checking for new emails...");
            // TODO: Implement IMAP/GraphAPI logic here
            // For now, this is a placeholder that might be triggered by a controller or background worker
            await Task.CompletedTask;
        }

        public async Task<IncomingInvoice> ProcessEmailAttachmentAsync(string filePath, string senderEmail)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAIExtractorService>();

            // 1. Create initial record
            var invoice = new IncomingInvoice
            {
                OriginalFilePath = filePath,
                Status = "Processing",
                CreatedAt = DateTime.UtcNow
            };
            
            context.IncomingInvoices.Add(invoice);
            await context.SaveChangesAsync();

            try 
            {
                // 2. Call AI Service
                var extractedData = await aiService.ExtractDataAsync(filePath);

                // 3. Match Vendor
                var vendor = await context.Vendors
                    .FirstOrDefaultAsync(v => v.Email == senderEmail || v.Name == extractedData.VendorName);

                if (vendor == null && !string.IsNullOrEmpty(extractedData.VendorName))
                {
                    // Create new vendor if not found (optional, maybe flag for review instead)
                    vendor = new Vendor
                    {
                        Name = extractedData.VendorName,
                        Email = senderEmail,
                        VATNumber = extractedData.VendorVAT ?? ""
                    };
                    context.Vendors.Add(vendor);
                    await context.SaveChangesAsync();
                }

                // 4. Update Invoice with Extracted Data
                invoice.VendorId = vendor?.Id;
                invoice.InvoiceNumber = extractedData.InvoiceNumber;
                invoice.InvoiceDate = extractedData.InvoiceDate;
                invoice.DueDate = extractedData.DueDate;
                invoice.TotalAmount = extractedData.TotalAmount;
                invoice.VATAmount = extractedData.VATAmount;
                invoice.ReviewConfidence = extractedData.ConfidenceScore;
                invoice.Status = "NeedsReview";
                
                // Add Lines
                foreach(var line in extractedData.Lines)
                {
                   line.IncomingInvoiceId = invoice.Id;
                   context.IncomingInvoiceLines.Add(line);
                }

                await context.SaveChangesAsync();
                return invoice;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invoice {Id}", invoice.Id);
                invoice.Status = "Error";
                invoice.DetectedIssues = ex.Message;
                await context.SaveChangesAsync();
                throw;
            }
        }
    }
}
