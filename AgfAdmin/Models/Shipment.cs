using System;

namespace AgfAdmin.Models
{
    public class Shipment
    {
        public int Id { get; set; }
        public string ContainerCode { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
        public DateTime EstimatedTimeOfArrival { get; set; }
        public string Status { get; set; } = "In Transit"; // e.g., On Truck, At Port, Sailing
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
