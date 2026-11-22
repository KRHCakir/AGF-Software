namespace AgfAdmin.Models
{
    // Certificaat van een relatie (certificaten deel 1): GLOBALG.A.P. (GGN), CoC, SKAL en andere, of een eigen soort. Hoort bij een relatie
    // (leverancier, klant, vervoerder) of, zonder relatie, bij het eigen bedrijf. Een relatie kan meerdere certificaten van dezelfde soort
    // hebben (twee GGN's voor verschillende producten). De status (geldig, verloopt binnenkort, verlopen) volgt uit de einddatum en wordt
    // nooit opgeslagen. Nooit verwijderd: archiveren (IsActive = false), want er hangen bestanden en bevestigingen aan (regel 33).
    public sealed class RelationCertificate
    {
        public int Id { get; set; }
        public int? RelationId { get; set; } // null = het eigen bedrijf
        public Relation Relation { get; set; }

        public string TypeCode { get; set; }        // een code uit CertificateTypes, of Custom
        public string CustomTypeName { get; set; }  // alleen bij Custom: de eigen soort
        public string Number { get; set; }          // zoals ingevoerd (GGN: 13 cijfers zonder spaties)
        public string NumberKey { get; set; }       // kleine letters zonder spaties en leestekens: zoeken op nummer
        public string CertificationBody { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidUntil { get; set; }

        // Overgenomen uit de oude GGN-velden van een leverancier zonder producten: "producten nog invullen". Gaat uit zodra er producten zijn.
        public bool ProductsPending { get; set; }
        // Uit de oude GGN-velden, zodat niets verloren gaat: producent en of die het GGN-label mag voeren.
        public string ProducerName { get; set; }
        public bool HasGgnLabel { get; set; }

        // Handmatige controle (bijvoorbeeld in het GLOBALG.A.P.-portaal): wanneer en door wie (CurrentUser; bij overgenomen GGN's de oude tekst).
        public DateOnly? CheckedOn { get; set; }
        public string CheckedBy { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        // Herkomst bij de migratie: het oude leveranciers-GGN (uniek, ligt vast).
        public int? SourceVendorGgnId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; }

        public ICollection<RelationCertificateProduct> Products { get; set; } = new List<RelationCertificateProduct>();
        public ICollection<RelationCertificateFile> Files { get; set; } = new List<RelationCertificateFile>();
    }

    // Voor welke producten een certificaat geldt: op productniveau (avocado), niet per variant, maat of herkomst.
    public sealed class RelationCertificateProduct
    {
        public int RelationCertificateId { get; set; }
        public RelationCertificate Certificate { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
    }

    // Een PDF of foto van een certificaat. Elke upload is een nieuwe versie; nooit gewijzigd of verwijderd (regel 33), de hash laat zien
    // dat het bestand niet veranderd is. Opslag zoals bij facturen (IDocumentStore).
    public sealed class RelationCertificateFile
    {
        public int Id { get; set; }
        public int RelationCertificateId { get; set; }
        public RelationCertificate Certificate { get; set; }
        public int Version { get; set; }
        public string FileName { get; set; }
        public string MediaType { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string StorageKey { get; set; }
        // Een HEIC-foto als JPEG voor de browser; anders leeg.
        public string DisplayStorageKey { get; set; }
        public string DisplayMediaType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        // Certificaten deel 2: deze versie is het origineel van een goedgekeurd AI-voorstel (met model, versie, wie en wanneer); anders leeg.
        public int? AiExtractionId { get; set; }
        public AiExtraction AiExtraction { get; set; }
    }

    // "Klopt dit?" bij inkoop: een product dat niet in het GGN-certificaat van de leverancier staat, of een certificaat dat verlopen is of
    // voor de levering verloopt. Een waarschuwing, geen blokkade: wie bevestigde, wanneer, bij welke order of ontvangst en wat er stond.
    // Append-only (regel 33).
    public sealed class CertificateConfirmation
    {
        public int Id { get; set; }
        public string Context { get; set; }               // PurchaseOrder of Receipt
        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }
        public int? PalletContentLineId { get; set; }     // bij ontvangst
        public PalletContentLine PalletContentLine { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string Kinds { get; set; }                 // NotCovered, Expired, ExpiresBeforeDelivery (komma-lijst)
        public string CertificateIds { get; set; }        // de GGN-certificaten van de leverancier op dat moment (komma-lijst)
        public string Message { get; set; }               // de tekst van de vraag zoals getoond
        public string ConfirmedBy { get; set; }
        public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
    }
}
