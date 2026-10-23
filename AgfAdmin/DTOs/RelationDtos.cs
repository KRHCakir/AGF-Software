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
        // Welke documenten naar de rollen verwijzen en welke defaults daar daadwerkelijk door gebruikt worden.
        public RelationUsageDto Usage { get; set; } = new();
    }

    public sealed class CustomerRoleDto
    {
        public int CustomerId { get; set; }
        // Verkoopdefault: betaaltermijn in dagen (gebruikt door de legacy orderroute; de nieuwe verkooporder kent nog geen betaaltermijn).
        public int PaymentTermsDays { get; set; }
    }

    public sealed class VendorRoleDto
    {
        public int VendorId { get; set; }
        // Inkoopdefaults: voorgesteld op een nieuwe inkoopfactuur, per document aanpasbaar en daar vastgelegd.
        public int PaymentTermsDays { get; set; }
        public string DefaultCurrency { get; set; }
        public List<VendorGgnDto> Ggns { get; set; } = new();
    }

    public sealed class VendorGgnDto
    {
        public int? Id { get; set; }
        public string Ggn { get; set; }
        public string ProducerName { get; set; }
        public bool IsActive { get; set; } = true;
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
        public SaveCustomerRoleDto Customer { get; set; }
        public SaveVendorRoleDto Vendor { get; set; }
        public List<RelationAddressDto> Addresses { get; set; }
        public List<RelationContactDto> Contacts { get; set; }
    }

    public sealed class SaveCustomerRoleDto
    {
        public int? PaymentTermsDays { get; set; }
    }

    public sealed class SaveVendorRoleDto
    {
        public int? PaymentTermsDays { get; set; }
        public string DefaultCurrency { get; set; }
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
