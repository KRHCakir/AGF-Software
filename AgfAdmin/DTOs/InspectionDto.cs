namespace AgfAdmin.DTOs
{
    public class InspectionDto
    {
        public int Score { get; set; }
        public string? Comment { get; set; }
        public string Inspector { get; set; } = string.Empty;
    }
}
