namespace AgfAdmin.Models
{
    // Logistieke drager. Nooit zelf de voorraadeigenaar: voorraad en partij zitten op de inhoudsregels.
    // Netto-kg wordt niet opgeslagen maar later berekend als som van de inhoudsregels.
    public sealed class Pallet
    {
        public int Id { get; set; }

        public string Reference { get; set; }      // verplicht, uniek, scanbaar
        public string Sscc { get; set; }           // optioneel, uniek indien aanwezig; alleen een echte gescande/ingevoerde GS1-SSCC
        public string PalletType { get; set; }     // optioneel

        public decimal? TareKg { get; set; }        // gemeten, nooit afgeleid
        public decimal? GrossKg { get; set; }       // gemeten, nooit afgeleid
        public decimal? DeclaredNetKg { get; set; } // opgegeven/sticker-waarde, apart van de berekende som

        public string LogisticsStatus { get; set; } // Expected, InTransit, Received, Dispatched, Delivered, Cancelled
        public string LocationContext { get; set; } // Supplier, Warehouse, Crossdock, InTransit, Customer, External
        public string WarehouseLocation { get; set; } // optioneel; alleen in het eigen magazijn

        public int? StockReceiptId { get; set; }
        public StockReceipt StockReceipt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token: start op 0 en wordt centraal in AppDbContext verhoogd bij elke wijziging.
        public int Version { get; set; }

        public ICollection<PalletContentLine> ContentLines { get; set; } = new List<PalletContentLine>();
    }
}
