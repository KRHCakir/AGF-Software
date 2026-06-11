using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.Models
{
    public class CompanySettings
    {
        [Key]
        public int Id { get; set; }
        
        public string CompanyName { get; set; } = "Mijn AGF Bedrijf";
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? KvkNumber { get; set; }
        public string? VatNumber { get; set; }
        public string? Iban { get; set; }
        
        // Hier slaan we straks de bestandsnaam of URL van het logo op
        public string? LogoPath { get; set; } 
    }
}