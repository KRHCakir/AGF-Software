// Invoice.cs
namespace AgfAdmin.Models
{
    // Verkoopfactuur. Eén model voor de oude facturen (Kind leeg: oude order of oude directe factuur, blijven staan zoals ze zijn) en de
    // verkoopfactuur van verkoopfactuur 1 (Kind Delivery = uit één of meer leveringen van een verkooporder, Kind Direct = zonder order).
    // Een nieuwe factuur begint als Concept zonder nummer; Definitief kent het nummer toe (VF-jjjj-nnnnn, doorlopend per jaar) en legt een
    // momentopname vast van klant, regels en bedragen. Daarna verandert er niets meer (TraceabilityGuard regel 44 en een databasetrigger).
    public class Invoice
    {
        public int Id { get; set; }
        public int? OrderId { get; set; }
        public Order? Order { get; set; }

        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        // Klantnaam: bij het nieuwe model de momentopname (bij aanmaken gevuld, bij Definitief opnieuw vastgelegd).
        public string? CustomCustomerName { get; set; }
        // Nieuw model: leeg tot Definitief.
        public string? InvoiceNumber { get; set; }
        // Nieuw model: de datum van Definitief maken (tot dan de aanmaakdatum).
        public DateTime InvoiceDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal VATAmount { get; set; }
        public decimal TotalAmount { get; set; }
        // Oud: Draft, Nieuw, Finalized. Nieuw model: Concept, Final of Discarded (SalesInvoiceRules).
        public string Status { get; set; } = "Draft";
        public string PaymentStatus { get; set; } = "Pending"; // Financial status (Pending, Paid, Overdue)
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- Nieuwe AGF Specifieke Velden ---
        // Nieuw model: de leverdatum (uit de verzending van de levering, of opgegeven bij een directe factuur).
        public DateTime? DeliveryDate { get; set; }
        public string? BatchNumber { get; set; } // Partij / Lotnummer: sinds feedback 22 november het nummer van de eigen verkoop-partij
        // De eigen verkoop-partij van deze directe factuur (Partij.InvoiceId); bij aanmaken toegekend, nooit gelijk aan een bestaand nummer.
        public Partij? SalesPartij { get; set; }
        public string? DeliveryAddress { get; set; }
        public string? ShippingMethod { get; set; }
        public string? ReferenceNumber { get; set; } // Uw bestelnummer / PO

        // --- Verkoopfactuur 1 (nieuw model; alle velden leeg bij oude facturen) ---
        public string? Kind { get; set; }
        // Idempotentiesleutel van het aanmaakcommando (uniek) en de vingerafdruk van die aanvraag (SHA-256 van de genormaliseerde inhoud):
        // een herhaling met dezelfde sleutel geeft alleen bij precies dezelfde aanvraag dezelfde factuur terug.
        public Guid? RequestId { get; set; }
        public string? RequestFingerprint { get; set; }
        // Doorlopend nummer per jaar, alleen bij Definitief (uniek samen).
        public int? InvoiceYear { get; set; }
        public int? InvoiceSequence { get; set; }
        public DateTime? FinalizedAt { get; set; }
        public string? FinalizedBy { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? DiscardedAt { get; set; }
        public string? DiscardedBy { get; set; }
        public string? DiscardReason { get; set; }
        // Momentopname van de klant bij Definitief: factuuradres en btw-nummer; betalingstermijn in dagen (vervaldatum = DueDate).
        public string? CustomerAddress { get; set; }
        public string? CustomerVatNumber { get; set; }
        public int? PaymentTermDays { get; set; }
        public DateTime? UpdatedAt { get; set; }
        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }

        // Navigation property for lines
        public ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();
    }
}
