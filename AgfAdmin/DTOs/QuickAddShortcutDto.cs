namespace AgfAdmin.DTOs
{
    public class QuickAddShortcutDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string? VariantName { get; set; }
        public string? Origin { get; set; }
        public string? Season { get; set; }
        public string? Notes { get; set; }
        public string? SizeCategory { get; set; }
        public string ShortcutCode { get; set; } = string.Empty;
    }
}