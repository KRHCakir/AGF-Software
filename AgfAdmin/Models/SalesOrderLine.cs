namespace AgfAdmin.Models
{
    // Klantvraag per variant + eenheid (uniek per order). Reserveringen hangen aan deze regel.
    public sealed class SalesOrderLine
    {
        public int Id { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public decimal Quantity { get; set; } // > 0, decimal(18,3)
        public string Unit { get; set; }      // box, colli, tray, piece of dozen; nooit omgerekend

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token; wordt bij elke reservering "aangeraakt".
        public int Version { get; set; }

        public ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();
    }
}
