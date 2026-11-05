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

        // Invoer inkoopfacturen (één bedrijf; er is geen tenantstructuur): ingeschakelde optionele velden en voorstellen.
        public string PurchaseInvoiceOptionalFields { get; set; } = Services.PurchaseInvoiceRules.DefaultOptionalFields;
        public decimal PurchaseInvoiceDefaultVatPercentage { get; set; } = Services.PurchaseInvoiceRules.DefaultVatPercentage;
        public int PurchaseInvoiceDefaultPaymentTermsDays { get; set; } = Services.PurchaseInvoiceRules.DefaultPaymentTermsDays;
        public string PurchaseInvoiceDefaultCurrency { get; set; } = Services.PurchaseInvoiceRules.DefaultCurrency;

        // Link naar de GLOBALG.A.P. Supply Chain Portal voor de handmatige GGN-controle (absolute https-URL; alleen een link).
        public string GlobalGapPortalUrl { get; set; } = Services.RelationRules.DefaultGlobalGapPortalUrl;

        // Partijnummering (PartijRules): Auto = het systeem stelt het volgende nummer voor (overschrijven mag), Own = alleen zelf typen.
        // Formaat met de plaatshouders {JJ}, {JJJJ}, {MM} en {volgnr}; het volgnummer telt opnieuw per jaar, per maand of nooit.
        public string PartijNumberMode { get; set; } = Services.PartijRules.ModeAuto;
        public string PartijNumberFormat { get; set; } = Services.PartijRules.DefaultFormat;
        public string PartijSequenceReset { get; set; } = Services.PartijRules.ResetYearly;
    }
}