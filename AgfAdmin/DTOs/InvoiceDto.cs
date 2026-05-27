using System;

namespace AgfAdmin.DTOs
{
    public class InvoiceDto
    {
        public int Id { get; set; }
        public int? OrderId { get; set; }
        public string OrderCustomerName { get; set; }
        public string OrderProductName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}