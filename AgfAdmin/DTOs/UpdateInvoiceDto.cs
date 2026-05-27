using System;

namespace AgfAdmin.DTOs
{
    public class UpdateInvoiceDto
    {
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public DateTime? DueDate { get; set; }
    }
} 