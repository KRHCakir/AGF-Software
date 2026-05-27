// Order.cs (updated)
namespace AgfAdmin.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        public string? CustomCustomerName { get; set; }

        public List<OrderLine> Lines { get; set; } = new List<OrderLine>();

        // Optional total computation for dashboard displays
        public decimal TotalAmount => Lines?.Sum(l => l.LineTotal) ?? 0;

        public string? ContainerCode { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Inspection? Inspection { get; set; }
    }
}