using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    public class MockAIExtractorService : IAIExtractorService
    {
        public async Task<ExtractedInvoiceAuthData> ExtractDataAsync(string filePath)
        {
            // Simulate AI processing delay
            await Task.Delay(1500);

            // Return mock data
            return new ExtractedInvoiceAuthData
            {
                InvoiceNumber = "INV-" + new Random().Next(1000, 9999),
                InvoiceDate = DateTime.UtcNow.AddDays(-5),
                DueDate = DateTime.UtcNow.AddDays(25),
                TotalAmount = 1250.00m,
                VATAmount = 250.00m,
                VendorName = "Tech Solutions Inc.",
                VendorVAT = "US123456789",
                ConfidenceScore = 0.92,
                Warnings = new List<string>(),
                Lines = new List<IncomingInvoiceLine>
                {
                    new IncomingInvoiceLine
                    {
                        Description = "Software License",
                        Quantity = 1,
                        UnitPrice = 1000.00m,
                        LineTotal = 1000.00m,
                        VATPercentage = 25.0m,
                        VATAmount = 250.00m
                    }
                }
            };
        }
    }
}
