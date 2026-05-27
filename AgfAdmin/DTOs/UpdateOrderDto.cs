namespace AgfAdmin.DTOs
{
    public class UpdateOrderDto
    {
        public string? ContainerCode { get; set; }
        public string Status { get; set; }  // "Pending", "Approved", "Shipped"
    }
}