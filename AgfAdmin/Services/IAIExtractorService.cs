using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    public interface IAIExtractorService
    {
        Task<ExtractedInvoiceAuthData> ExtractDataAsync(string filePath);
    }

    public class ExtractedInvoiceAuthData
    {
        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal VATAmount { get; set; }
        public string? VendorName { get; set; }
        public string? VendorVAT { get; set; }
        public double ConfidenceScore { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<IncomingInvoiceLine> Lines { get; set; } = new();
    }
}
