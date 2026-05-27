namespace AgfAdmin.Models
{
    public class Inspection
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order Order { get; set; }

        public int Score { get; set; }  // 1-10
        public string? Comment { get; set; }
        public string Inspector { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}