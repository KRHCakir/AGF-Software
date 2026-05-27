using System;

namespace AgfAdmin.DTOs
{
    public class CreateInvoiceDto
    {
        public int OrderId { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime? DueDate { get; set; }
    }
}