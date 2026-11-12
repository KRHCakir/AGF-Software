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
        // Aantal cijfers van het volgnummer (minimaal 1, standaard 3: 52611002).
        public int PartijSequenceDigits { get; set; } = Services.PartijRules.DefaultSequenceDigits;
        // Tweede reeks voor verkoop-partijen: eigen formaat, eigen aantal cijfers (2 geeft 261140) en eigen teller; de modus en het
        // opnieuw tellen zijn dezelfde instelling als hierboven.
        public string SalesPartijNumberFormat { get; set; } = Services.PartijRules.DefaultSalesFormat;
        public int SalesPartijSequenceDigits { get; set; } = Services.PartijRules.DefaultSequenceDigits;

        // AI-lezen van inkoopfacturen (Beheer): standaard uit. Aanzetten stuurt documenten naar een externe dienst; de beheerder bevestigt
        // dan dat de klant daarvan op de hoogte is (wie en wanneer). Boven de maandlimiet in euro pauzeert het lezen; handmatig blijft werken.
        public bool AiReadingEnabled { get; set; }
        public string AiReadingEnabledBy { get; set; }
        public DateTime? AiReadingEnabledAt { get; set; }
        public decimal AiReadingMonthlyLimitEur { get; set; } = Services.DocumentAi.AiReadingRules.DefaultMonthlyLimitEur;
    }
}