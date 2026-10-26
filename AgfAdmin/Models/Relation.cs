namespace AgfAdmin.Models
{
    // Relatie: één onderneming met gedeelde bedrijfsgegevens, die klant (Customer), leverancier (Vendor) of beide is.
    // De rollen blijven eigen tabellen met hun bestaande ID's en rolspecifieke afspraken (betaaltermijn, valuta, GGN's);
    // alle gedeelde gegevens (naam, btw, adressen, contacten) hebben hier hun enige wijzigbare bron. De gelijknamige
    // kolommen op Customer/Vendor zijn een spiegel die uitsluitend RelationService schrijft (legacy lezers en documenten
    // blijven werken). Nooit automatisch samenvoegen op naam, e-mail of btw-nummer.
    public sealed class Relation
    {
        public int Id { get; set; }

        // Idempotentiesleutel van het aanmaakcommando (uniek, null voor gemigreerde relaties) en de canonieke payload voor de replayvergelijking.
        public Guid? RequestId { get; set; }
        public string RequestPayload { get; set; }

        // Zoekcode: optioneel, hoofdletterongevoelig uniek (SearchCodeKey = kleine letters, unieke index).
        public string SearchCode { get; set; }
        public string SearchCodeKey { get; set; }

        public string Name { get; set; }          // volledige bedrijfsnaam (verplicht, niet uniek)
        public string TradeName { get; set; }     // handelsnaam (optioneel)
        public string PreferredLanguage { get; set; } // nl, en, de, fr, es, … (optioneel)
        public string Website { get; set; }

        // Algemene contactgegevens van de onderneming (naast contactpersonen).
        public string Email { get; set; }
        public string Phone { get; set; }

        public string VatNumber { get; set; }
        // VIES-voorbereiding: status van het gecontroleerde nummer. Deze PR doet geen externe aanvraag; alleen NotChecked en
        // NotApplicable komen voor. Een gewijzigd btw-nummer erft nooit de status van het vorige nummer.
        public string VatVerificationStatus { get; set; } = VatVerification.NotChecked;
        public string VatVerifiedNumber { get; set; }
        public DateTime? VatVerifiedAt { get; set; }

        // KvK-nummer of buitenlands registratienummer, met land en type (KvK, HRB, SIRET, …).
        public string RegistrationNumber { get; set; }
        public string RegistrationCountry { get; set; }
        public string RegistrationType { get; set; }
        public string LegalForm { get; set; }

        public string InternalNote { get; set; }

        // Inactief in plaats van verwijderen zodra documenten naar een rol verwijzen: historie blijft leesbaar, nieuwe documenten weigeren.
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token: start op 0 en wordt centraal in AppDbContext verhoogd bij elke wijziging van de relatie.
        public int Version { get; set; }

        public Customer Customer { get; set; }
        public Vendor Vendor { get; set; }
        public ICollection<RelationAddress> Addresses { get; set; } = new List<RelationAddress>();
        public ICollection<RelationContact> Contacts { get; set; } = new List<RelationContact>();
        public ICollection<RelationBankAccount> BankAccounts { get; set; } = new List<RelationBankAccount>();
    }

    public static class VatVerification
    {
        public const string NotChecked = "NotChecked";
        public const string Valid = "Valid";
        public const string Invalid = "Invalid";
        public const string Unavailable = "Unavailable";
        public const string NotApplicable = "NotApplicable";
        public static readonly IReadOnlyList<string> All = new[] { NotChecked, Valid, Invalid, Unavailable, NotApplicable };
    }

    // Adres met doel: Main (hoofd/bezoek), Billing (factuur) of Delivery (aflever/magazijn; meerdere, met één standaard).
    // Geschikt voor buitenlandse adressen: alleen land is verplicht voor nieuwe en gewijzigde adressen; postcode is vrije tekst.
    public sealed class RelationAddress
    {
        public int Id { get; set; }
        public int RelationId { get; set; }
        public Relation Relation { get; set; }

        public string Purpose { get; set; }   // Main, Billing, Delivery
        public string Label { get; set; }     // bijv. "Magazijn Venlo" (optioneel)
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string PostalCode { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string CountryCode { get; set; } // ISO 3166-1 alpha-2; null alleen bij uit vrije tekst overgenomen legacy-adressen
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }

    public static class AddressPurpose
    {
        public const string Main = "Main";
        public const string Billing = "Billing";
        public const string Delivery = "Delivery";
        public static readonly IReadOnlyList<string> All = new[] { Main, Billing, Delivery };
    }

    // Contactpersoon met afdeling/functie en meerdere telefoonnummers en e-mailadressen (kanalen). Een functie bepaalt
    // niet welke documenten iemand ontvangt; documentontvangers volgen in een aparte stap.
    public sealed class RelationContact
    {
        public int Id { get; set; }
        public int RelationId { get; set; }
        public Relation Relation { get; set; }

        public string Name { get; set; }
        public string Department { get; set; } // verkoop, inkoop, administratie, transport, … (vrije tekst met suggesties)
        public string JobTitle { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; } = true;
        public string Note { get; set; }
        public int SortOrder { get; set; }

        public ICollection<RelationContactChannel> Channels { get; set; } = new List<RelationContactChannel>();
    }

    public sealed class RelationContactChannel
    {
        public int Id { get; set; }
        public int RelationContactId { get; set; }
        public RelationContact RelationContact { get; set; }

        public string Kind { get; set; }   // Phone, Email
        public string Label { get; set; }  // bijv. "mobiel", "kantoor", "facturen"
        public string Value { get; set; }
        public int SortOrder { get; set; }
    }

    public static class ContactChannelKind
    {
        public const string Phone = "Phone";
        public const string Email = "Email";
        public static readonly IReadOnlyList<string> All = new[] { Phone, Email };
    }

    // GLOBALG.A.P.-nummers van een leverancier: één leverancier kan meerdere producenten vertegenwoordigen. Zoals opgegeven;
    // certificeringsstatus niet gecontroleerd (formaatcontrole is geen certificaatcontrole). Geen CoC-nummer.
    public sealed class VendorGgn
    {
        public int Id { get; set; }
        public int VendorId { get; set; }
        public Vendor Vendor { get; set; }

        public string Ggn { get; set; }          // 13 cijfers als tekst
        public string ProducerName { get; set; } // optioneel
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        // Handmatige controle in de GLOBALG.A.P. Supply Chain Portal (link in de bedrijfsinstellingen): wanneer, door wie, tot wanneer
        // het certificaat geldig is en of de producent het GGN-label mag voeren. Zoals ingevoerd; de applicatie raadpleegt het portaal niet.
        public DateOnly? CheckedOn { get; set; }
        public string CheckedBy { get; set; }
        public DateOnly? CertificateValidUntil { get; set; }
        public bool HasGgnLabel { get; set; }
    }

    // Bankrekening van een relatie (crediteur of debiteur). Precies één van Iban (SEPA, genormaliseerd: hoofdletters zonder spaties)
    // en OtherAccountNumber (niet-SEPA). Elke toevoeging of wijziging van IBAN, rekeninghouder of ander rekeningnummer zet de rekening op
    // Unconfirmed en schrijft een auditregel; bevestigen is een aparte handeling met "wie". Nooit verwijderen: inactief zetten.
    // Per relatie hooguit één actieve standaardrekening (gefilterde unieke index + regel).
    public sealed class RelationBankAccount
    {
        public int Id { get; set; }
        public int RelationId { get; set; }
        public Relation Relation { get; set; }

        public string Iban { get; set; }
        public string OtherAccountNumber { get; set; }
        public string Bic { get; set; }
        public string AccountHolder { get; set; }
        public string Currency { get; set; } = "EUR";
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public string ConfirmationStatus { get; set; } = BankAccountConfirmation.Unconfirmed;
        public DateTime? ConfirmedAt { get; set; }
        public string ConfirmedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<RelationBankAccountEvent> Events { get; set; } = new List<RelationBankAccountEvent>();
    }

    public static class BankAccountConfirmation
    {
        public const string Unconfirmed = "Unconfirmed";
        public const string Confirmed = "Confirmed";
        public static readonly IReadOnlyList<string> All = new[] { Unconfirmed, Confirmed };
    }

    // Append-only audit van bankgegevens (TraceabilityGuard regel 3): wie (vrije tekst, er zijn geen gebruikersaccounts), wanneer,
    // wat, met oude en nieuwe IBAN / rekeninghouder / ander rekeningnummer.
    public sealed class RelationBankAccountEvent
    {
        public int Id { get; set; }
        public int RelationBankAccountId { get; set; }
        public RelationBankAccount BankAccount { get; set; }
        public int RelationId { get; set; }
        public Relation Relation { get; set; }

        public string Kind { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        public string OldIban { get; set; }
        public string NewIban { get; set; }
        public string OldAccountHolder { get; set; }
        public string NewAccountHolder { get; set; }
        public string OldOtherAccountNumber { get; set; }
        public string NewOtherAccountNumber { get; set; }
    }

    public static class BankAccountEventKind
    {
        public const string Added = "Added";
        public const string Changed = "Changed";
        public const string Confirmed = "Confirmed";
        public const string Deactivated = "Deactivated";
        public static readonly IReadOnlyList<string> All = new[] { Added, Changed, Confirmed, Deactivated };
    }

    // Rolafspraken die klant- en leveranciersrol delen (betaalwijze, levering, handelsvoorwaarden). Regels in RoleTermsRules.
    public interface IRoleTerms
    {
        string PaymentMethod { get; set; }
        string MandateReference { get; set; }
        DateOnly? MandateSignedOn { get; set; }
        string DeliveryTermKind { get; set; }
        string Incoterm { get; set; }
        string IncotermPlace { get; set; }
        string DeliveryTermText { get; set; }
        string TradeConditions { get; set; }
        string TradeConditionsText { get; set; }
    }

    public static class PaymentMethods
    {
        public const string BankTransfer = "BankTransfer";
        public const string DirectDebit = "DirectDebit";
        public const string Cash = "Cash";
        public const string Offset = "Offset";
        public static readonly IReadOnlyList<string> All = new[] { BankTransfer, DirectDebit, Cash, Offset };
    }

    // Leveringsconditie: een Incoterm 2020 met plaats, of een eigen term (Franco, Af kwekerij, vrije tekst).
    public static class DeliveryTermKinds
    {
        public const string Incoterm = "Incoterm";
        public const string Franco = "Franco";
        public const string AfKwekerij = "AfKwekerij";
        public const string Other = "Other";
        public static readonly IReadOnlyList<string> All = new[] { Incoterm, Franco, AfKwekerij, Other };
    }

    public static class TradeConditionKinds
    {
        public const string Own = "Own";
        public const string Frugiventa = "Frugiventa";
        public const string Cofreurop = "Cofreurop";
        public const string Other = "Other";
        public static readonly IReadOnlyList<string> All = new[] { Own, Frugiventa, Cofreurop, Other };
    }

    public static class CreditInsurers
    {
        public const string Atradius = "Atradius";
        public const string AllianzTrade = "AllianzTrade";
        public const string Coface = "Coface";
        public const string Other = "Other";
        public static readonly IReadOnlyList<string> All = new[] { Atradius, AllianzTrade, Coface, Other };
    }
}
