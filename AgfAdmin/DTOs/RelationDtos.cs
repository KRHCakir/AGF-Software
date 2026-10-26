namespace AgfAdmin.DTOs
{
    // Relatiewerkplek (api/relations): één onderneming die klant, leverancier of beide is. Rollen houden hun bestaande
    // Customer-/Vendor-ID's; gedeelde gegevens hebben hier hun enige wijzigbare bron.

    // Compacte lijstregel (filters Alle / Klanten / Leveranciers, zoeken op code, naam, handelsnaam, btw, plaats, contact).
    public sealed class RelationListItemDto
    {
        public int Id { get; set; }
        public string SearchCode { get; set; }
        public string Name { get; set; }
        public string TradeName { get; set; }
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }
        public bool IsActive { get; set; }
        public string City { get; set; }
        public string CountryCode { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string PrimaryContactName { get; set; }
        public string VatNumber { get; set; }
        public int Version { get; set; }
    }

    public sealed class RelationListDto
    {
        public List<RelationListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // Volledige relatiekaart.
    public sealed class RelationDto
    {
        public int Id { get; set; }
        public string RequestId { get; set; }
        public string SearchCode { get; set; }
        public string Name { get; set; }
        public string TradeName { get; set; }
        public string PreferredLanguage { get; set; }
        public string Website { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string VatNumber { get; set; }
        public string VatVerificationStatus { get; set; }
        public string VatVerifiedNumber { get; set; }
        public DateTime? VatVerifiedAt { get; set; }
        public string RegistrationNumber { get; set; }
        public string RegistrationCountry { get; set; }
        public string RegistrationType { get; set; }
        public string LegalForm { get; set; }
        public string InternalNote { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Version { get; set; }
        public CustomerRoleDto Customer { get; set; }   // null = geen klantrol
        public VendorRoleDto Vendor { get; set; }       // null = geen leveranciersrol
        public List<RelationAddressDto> Addresses { get; set; } = new();
        public List<RelationContactDto> Contacts { get; set; } = new();
        // Bankrekeningen (actief en inactief, op volgorde) en de laatste 50 auditregels van bankgegevens (nieuwste eerst).
        public List<RelationBankAccountDto> BankAccounts { get; set; } = new();
        public List<RelationBankAccountEventDto> BankAccountEvents { get; set; } = new();
        // Niet-blokkerende signalen: blijvend (bijv. standaardrekening onbevestigd, verzekering verlopen) en, alleen in het antwoord
        // op een opslag, eenmalig (e-mailtikfout, domein zonder mailserver).
        public List<RelationWarningDto> Warnings { get; set; } = new();
        // Welke documenten naar de rollen verwijzen en welke defaults daar daadwerkelijk door gebruikt worden.
        public RelationUsageDto Usage { get; set; } = new();
    }

    public sealed class CustomerRoleDto
    {
        public int CustomerId { get; set; }
        // Betalingsconditie (verplicht) en de daarvan afgeleide termijn in dagen (spiegel; gebruikt door de legacy orderroute).
        public int PaymentConditionId { get; set; }
        public PaymentConditionSummaryDto PaymentCondition { get; set; }
        public int PaymentTermsDays { get; set; }
        // Vrije termijn voor het formulier (afgeleid van de conditie): einde maand en optionele korting.
        public bool PaymentTermsEndOfMonth { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public string PaymentMethod { get; set; }
        public string MandateReference { get; set; }
        public DateOnly? MandateSignedOn { get; set; }
        public string Currency { get; set; }
        public string DeliveryTermKind { get; set; }
        public string Incoterm { get; set; }
        public string IncotermPlace { get; set; }
        public string DeliveryTermText { get; set; }
        public string TradeConditions { get; set; }
        public string TradeConditionsText { get; set; }
        // Krediet (EUR).
        public decimal? CreditLimit { get; set; }
        public bool IsInsured { get; set; }
        public string Insurer { get; set; }
        public string InsurerText { get; set; }
        public decimal? InsuredLimit { get; set; }
        public string InsurerReference { get; set; }
        public DateOnly? InsuredUntil { get; set; }
    }

    public sealed class VendorRoleDto
    {
        public int VendorId { get; set; }
        // Inkoopdefaults: voorgesteld op een nieuwe inkoopfactuur, per document aanpasbaar en daar vastgelegd.
        public int PaymentConditionId { get; set; }
        public PaymentConditionSummaryDto PaymentCondition { get; set; }
        public int PaymentTermsDays { get; set; }
        public bool PaymentTermsEndOfMonth { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public string DefaultCurrency { get; set; }
        public string PaymentMethod { get; set; }
        public string MandateReference { get; set; }
        public DateOnly? MandateSignedOn { get; set; }
        public string DeliveryTermKind { get; set; }
        public string Incoterm { get; set; }
        public string IncotermPlace { get; set; }
        public string DeliveryTermText { get; set; }
        public string TradeConditions { get; set; }
        public string TradeConditionsText { get; set; }
        public List<VendorGgnDto> Ggns { get; set; } = new();
    }

    public sealed class PaymentConditionSummaryDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string DescriptionNl { get; set; }
        public string DescriptionEn { get; set; }
        public int Days { get; set; }
        public string Basis { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class VendorGgnDto
    {
        public int? Id { get; set; }
        public string Ggn { get; set; }
        public string ProducerName { get; set; }
        public bool IsActive { get; set; } = true;
        // Handmatige controle in de GLOBALG.A.P. Supply Chain Portal (zoals ingevoerd).
        public DateOnly? CheckedOn { get; set; }
        public string CheckedBy { get; set; }
        public DateOnly? CertificateValidUntil { get; set; }
        public bool HasGgnLabel { get; set; }
    }

    // Bankrekening. Invoer: id (bestaand) of null (nieuw); iban óf otherAccountNumber; bic, rekeninghouder, valuta, standaard, actief.
    // Bevestigingsvelden zijn alleen uitvoer (bevestigen via POST .../bank-accounts/{bankId}/confirm).
    public sealed class RelationBankAccountDto
    {
        public int? Id { get; set; }
        public string Iban { get; set; }
        public string OtherAccountNumber { get; set; }
        public string Bic { get; set; }
        public string AccountHolder { get; set; }
        public string Currency { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public string ConfirmationStatus { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string ConfirmedBy { get; set; }
    }

    public sealed class RelationBankAccountEventDto
    {
        public int Id { get; set; }
        public int BankAccountId { get; set; }
        public string Kind { get; set; }  // Added, Changed, Confirmed, Deactivated
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; }
        public string OldIban { get; set; }
        public string NewIban { get; set; }
        public string OldAccountHolder { get; set; }
        public string NewAccountHolder { get; set; }
        public string OldOtherAccountNumber { get; set; }
        public string NewOtherAccountNumber { get; set; }
    }

    // Niet-blokkerend signaal met vaste code (de frontend vertaalt op code), het veld waarop het slaat en eventueel een suggestie.
    public sealed class RelationWarningDto
    {
        public string Code { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
        public string Suggestion { get; set; }
    }

    // POST api/relations/{id}/bank-accounts/{bankId}/confirm
    public sealed class ConfirmBankAccountDto
    {
        public int? ExpectedVersion { get; set; }
        public string ActorReference { get; set; }
    }

    // GET/PUT api/relations/settings
    public sealed class RelationSettingsDto
    {
        public string GlobalGapPortalUrl { get; set; }
    }

    public sealed class RelationAddressDto
    {
        public int? Id { get; set; }
        public string Purpose { get; set; }
        public string Label { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string PostalCode { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string CountryCode { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class RelationContactDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string Department { get; set; }
        public string JobTitle { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; } = true;
        public string Note { get; set; }
        public List<ContactChannelDto> Channels { get; set; } = new();
    }

    public sealed class ContactChannelDto
    {
        public int? Id { get; set; }
        public string Kind { get; set; }  // Phone, Email
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public sealed class RelationUsageDto
    {
        public int SalesOrders { get; set; }
        public int LegacyOrders { get; set; }
        public int DirectTradeCommitments { get; set; }
        public int PurchaseInvoices { get; set; }
        public int SupplyCommitments { get; set; }
        // Welke rol-defaults door welke bestaande documenten gebruikt worden (alleen wat de code werkelijk doet).
        public List<string> DefaultsInUse { get; set; } = new();
    }

    // POST api/relations (RequestId verplicht, replay op canonieke payload) en PUT api/relations/{id} (ExpectedVersion verplicht).
    // Rollen: null = rol niet aanwezig / niet wijzigen; een bestaande rol wordt nooit verwijderd (alleen de relatie kan inactief).
    public sealed class SaveRelationDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedVersion { get; set; }
        public string SearchCode { get; set; }
        public string Name { get; set; }
        public string TradeName { get; set; }
        public string PreferredLanguage { get; set; }
        public string Website { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string VatNumber { get; set; }
        public string RegistrationNumber { get; set; }
        public string RegistrationCountry { get; set; }
        public string RegistrationType { get; set; }
        public string LegalForm { get; set; }
        public string InternalNote { get; set; }
        public bool? IsActive { get; set; }
        public bool AcknowledgeSimilarNames { get; set; }
        // Wie (vrije tekst, max 100): verplicht zodra bankgegevens wijzigen (toevoegen, IBAN/houder/nummer wijzigen, inactief zetten).
        public string ActorReference { get; set; }
        public SaveCustomerRoleDto Customer { get; set; }
        public SaveVendorRoleDto Vendor { get; set; }
        public List<RelationAddressDto> Addresses { get; set; }
        public List<RelationContactDto> Contacts { get; set; }
        // null = ongewijzigd laten; een lijst moet alle opgeslagen rekeningen bevatten (nooit verwijderen, inactief zetten).
        public List<RelationBankAccountDto> BankAccounts { get; set; }
    }

    // Rolvelden. Betalingstermijn op drie manieren:
    //   - vrije termijn (het relatieformulier): paymentTermsEndOfMonth meegestuurd (true/false) maakt de rol "vrij"; dan geldt
    //     paymentTermsDays leeg = standaard uit de bedrijfsinstellingen (CompanySettings.PurchaseInvoiceDefaultPaymentTermsDays),
    //     anders 0..365, en discountPercent + discountDays beide of geen van beide. De server zoekt of maakt de passende conditie
    //     (PaymentConditionResolver); komt de termijn overeen met de huidige conditie, dan blijft die staan. Niet samen met paymentConditionId.
    //   - paymentConditionId (bestaande clients): gekozen conditie.
    //   - alleen paymentTermsDays (legacy): alleen gebruikt als het afwijkt van de huidige termijn (dan "{n}D" na factuurdatum).
    // Lege optionele velden = leeg (PUT vervangt de kaart); paymentMethod/currency leeg = huidige waarde (nieuw: BankTransfer / EUR).
    public sealed class SaveCustomerRoleDto
    {
        public int? PaymentConditionId { get; set; }
        public int? PaymentTermsDays { get; set; }
        public bool? PaymentTermsEndOfMonth { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public string PaymentMethod { get; set; }
        public string MandateReference { get; set; }
        public DateOnly? MandateSignedOn { get; set; }
        public string Currency { get; set; }
        public string DeliveryTermKind { get; set; }
        public string Incoterm { get; set; }
        public string IncotermPlace { get; set; }
        public string DeliveryTermText { get; set; }
        public string TradeConditions { get; set; }
        public string TradeConditionsText { get; set; }
        public decimal? CreditLimit { get; set; }
        public bool IsInsured { get; set; }
        public string Insurer { get; set; }
        public string InsurerText { get; set; }
        public decimal? InsuredLimit { get; set; }
        public string InsurerReference { get; set; }
        public DateOnly? InsuredUntil { get; set; }
    }

    public sealed class SaveVendorRoleDto
    {
        public int? PaymentConditionId { get; set; }
        public int? PaymentTermsDays { get; set; }
        public bool? PaymentTermsEndOfMonth { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public string DefaultCurrency { get; set; }
        public string PaymentMethod { get; set; }
        public string MandateReference { get; set; }
        public DateOnly? MandateSignedOn { get; set; }
        public string DeliveryTermKind { get; set; }
        public string Incoterm { get; set; }
        public string IncotermPlace { get; set; }
        public string DeliveryTermText { get; set; }
        public string TradeConditions { get; set; }
        public string TradeConditionsText { get; set; }
        public List<VendorGgnDto> Ggns { get; set; }
    }

    // Keuzelijsten voor het formulier.
    public sealed class RelationReferenceDto
    {
        public List<string> Languages { get; set; } = new();
        public List<string> AddressPurposes { get; set; } = new();
        public List<string> DepartmentSuggestions { get; set; } = new();
        public List<string> RegistrationTypeSuggestions { get; set; } = new();
        public List<string> LegalFormSuggestions { get; set; } = new();
        public List<string> Currencies { get; set; } = new();
        public List<string> VatVerificationStatuses { get; set; } = new();
        public string GlobalGapDatabaseUrl { get; set; }
        // Bedrijfsinstelling: link naar de GLOBALG.A.P. Supply Chain Portal (handmatige GGN-controle).
        public string GlobalGapPortalUrl { get; set; }
        public List<string> PaymentMethods { get; set; } = new();
        public List<string> PaymentConditionBases { get; set; } = new();
        // Standaard betalingstermijn als het veld leeg blijft (bedrijfsinstelling, dezelfde als bij inkoopfacturen).
        public int DefaultPaymentTermsDays { get; set; }
        public List<string> DeliveryTermKinds { get; set; } = new();
        public List<string> Incoterms { get; set; } = new();
        public List<string> TradeConditions { get; set; } = new();
        public List<string> CreditInsurers { get; set; } = new();
        // Artikelen met een oude standaard-GGN die nog handmatig aan een leverancier moet worden toegewezen.
        public List<LegacyArticleGgnDto> LegacyArticleGgns { get; set; } = new();
    }

    public sealed class LegacyArticleGgnDto
    {
        public int ProductVariantId { get; set; }
        public string Description { get; set; }
        public string Ggn { get; set; }
    }

    public sealed class SimilarRelationDto
    {
        public int Id { get; set; }
        public string SearchCode { get; set; }
        public string Name { get; set; }
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }
    }
}
