using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.DTOs
{
    public class ParseOrderRequest
    {
        [Required]
        public string RawText { get; set; }
        public int? SellerId { get; set; } // Optional: context for who is selling used for AI context if needed
    }

    public class ParseOrderResponse
    {
        public List<ParsedOrderItem> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public double ConfidenceScore { get; set; }
    }

    public class ParsedOrderItem
    {
        public string OriginalTextSegment { get; set; }
        
        public int? ProductVariantId { get; set; }
        public string? ProductName { get; set; } // For display if ID not found or partial match
        
        public int Quantity { get; set; }
        public string? Unit { get; set; } // Box, Kg, etc.
        
        public decimal? EstimatedPrice { get; set; } // Optional
        public double Confidence { get; set; }
        
        public int? CurrentStock { get; set; }
    }

    public class ConfirmOrderRequest
    {
        public int? CustomerId { get; set; }
        public string? CustomCustomerName { get; set; }
        
        [Required]
        public List<ConfirmOrderItem> Items { get; set; }
    }

    public class ConfirmOrderItem
    {
        [Required]
        public int ProductVariantId { get; set; }
        
        [Required]
        public int Quantity { get; set; }
        
        public decimal? AgreedPrice { get; set; } // Override price
        
        public int? TargetShipmentId { get; set; } // Manual FIFO override
    }
}
