namespace AgfAdmin.DTOs
{
    // Partijen (PartijService): nummering, aanmaken, dubbelcontrole, correctie, verwijderen en koppelen aan een inkooporder.

    public sealed class PartijSettingsDto
    {
        public string Mode { get; set; }
        public string Format { get; set; }
        public string SequenceReset { get; set; }
        // Later toegevoegd (P2); weggelaten bij wijzigen = ongewijzigd. Formaat en aantal cijfers per reeks; modus en opnieuw tellen gedeeld.
        public int? SequenceDigits { get; set; }
        public string SalesFormat { get; set; }
        public int? SalesSequenceDigits { get; set; }
    }

    // Het volgende voorgestelde nummer (null in modus Own), eventueel voor een nog niet opgeslagen formaat (live voorbeeld).
    public sealed class PartijNextNumberDto
    {
        public string Kind { get; set; }
        public int SequenceDigits { get; set; }
        public string Mode { get; set; }
        public string Format { get; set; }
        public string SequenceReset { get; set; }
        public string Suggestion { get; set; }
    }

    public sealed class PartijSummaryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        // Purchase of Sales; bij Sales de verkooporder (1-op-1).
        public string Kind { get; set; }
        public int? SalesOrderId { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        // Er hangt een order, orderregel of palletregel aan: niet meer te verwijderen.
        public bool Linked { get; set; }
    }

    // Number leeg = het volgende voorgestelde nummer (alleen in modus Auto).
    public sealed class CreatePartijDto
    {
        public Guid? RequestId { get; set; }
        public string Number { get; set; }
        public string Note { get; set; }
    }

    // Nieuwe partij samen met een inkooporder; Number leeg = het voorgestelde nummer.
    public sealed class NewPartijForOrderDto
    {
        public string Number { get; set; }
    }

    public sealed class CorrectPartijNumberDto
    {
        public string Number { get; set; }
        public string Reason { get; set; }
    }

    // Een bestaande order (kop en regels) aan een partij koppelen. Na ontvangst alleen een order zonder partij, met reden.
    public sealed class LinkPartijDto
    {
        public int? SupplyCommitmentId { get; set; }
        public string Reason { get; set; }
    }

    // 409 bij een nummer dat al bestaat (zelfde sleutel): het scherm vraagt "Partij … bestaat al, wil je die gebruiken?".
    public sealed class PartijExistsConflictDto
    {
        public const string ExistsCode = "PARTIJ_EXISTS";

        public string Message { get; set; }
        public string Code { get; set; } = ExistsCode;
        public PartijSummaryDto Partij { get; set; }
    }
}

namespace AgfAdmin.DTOs
{
    // Lijst en detail (PartijService.Read.cs). Hoeveelheden in colli: de som van alle verpakkingen, zonder eenheden om te rekenen (zoals
    // de inkooporderlijst); Unit is de eenheid als die voor de hele partij gelijk is, anders null ("gemengd").
    public sealed class PartijListDto
    {
        public List<PartijListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class PartijTotalsDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        // Purchase of Sales. Bij Sales: de verkooporder (1-op-1), de klant, wat er besteld is (OrderedQuantity) en uit welke
        // inkoop-partijen de colli komen (SourcePartijen, via de reserveringen); de inkoopvelden blijven dan 0 of leeg.
        public string Kind { get; set; }
        public int? SalesOrderId { get; set; }
        public string SalesOrderNumber { get; set; }
        public string SalesOrderStatus { get; set; }
        public string CustomerName { get; set; }
        public decimal OrderedQuantity { get; set; }
        public List<string> SourcePartijen { get; set; } = new();
        // Open of SoldOut (afgeleid: niets meer op voorraad of in keuring en niets meer verwacht, na minstens één ontvangst).
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Vendors { get; set; } = new();
        public List<string> Products { get; set; } = new();
        public List<string> SupplierLotReferences { get; set; } = new();
        public string Unit { get; set; }
        // Besteld (regels van niet geannuleerde orders), fysiek ontvangen, goedgekeurd op voorraad, in keuring, nog verwacht en verkocht
        // (actief gereserveerd plus verzonden, ook klantgebonden toewijzingen).
        public decimal PurchasedQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal InInspectionQuantity { get; set; }
        public decimal OpenExpectedQuantity { get; set; }
        public decimal SoldQuantity { get; set; }
        // Netto kg: bij een inkoop-partij fysiek ontvangen, bij een verkoop-partij de kg van de orderregels (anders de gereserveerde kg).
        // Samen met ontvangen (inkoop) of besteld (verkoop) de basis voor "Verdeel naar kg" en "Verdeel naar colli".
        public decimal NetKg { get; set; }
    }

    public sealed class PartijListItemDto : PartijTotalsDto
    {
    }

    public sealed class PartijDetailDto : PartijTotalsDto
    {
        public string Note { get; set; }
        public string CreatedBy { get; set; }
        // Er hangt een order, orderregel of palletregel aan: nummer alleen via "Nummer corrigeren", niet meer te verwijderen.
        public bool Linked { get; set; }
        // Het nummer verandert alleen met reden: een inkoop-partij met koppelingen, of een verkoop-partij waarvan de order iets
        // gereserveerd of geleverd heeft (of geannuleerd is). Een verkoop-partij daarvoor mag zonder reden worden aangepast.
        public bool NumberFixed { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ShippedQuantity { get; set; }
        public List<PartijOrderDto> Orders { get; set; } = new();
        public List<PartijReceiptDto> Receipts { get; set; } = new();
        public List<PartijSalesOrderDto> SalesOrders { get; set; } = new();
        // Alleen bij een verkoop-partij: per inkoop-partij en eenheid wat eruit gereserveerd of verzonden is.
        public List<PartijSourceDto> Sources { get; set; } = new();
        // Kosten op deze partij (P2), per factuurregel; het totaal is alleen van deze partij.
        public List<PartijCostLineDto> Costs { get; set; } = new();
        public decimal CostTotal { get; set; }
        // Apart en niet in CostTotal, zodat niets dubbel telt: de kosten op de verkoop-partijen die colli uit deze inkoop-partij pakken,
        // of (bij een verkoop-partij) op de inkoop-partijen waar de colli vandaan komen.
        public List<PartijLinkedCostDto> LinkedCosts { get; set; } = new();
        public List<PartijHistoryDto> History { get; set; } = new();
    }

    public sealed class PartijCostLineDto
    {
        public string Kind { get; set; }
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string VendorName { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public string Description { get; set; }
        // In euro (altijd).
        public decimal Amount { get; set; }
        // Bij een factuur in een andere valuta: die valuta, het bedrag daarin (omgerekend met de ECB-koers van de factuur) en de koers.
        public string Currency { get; set; }
        public decimal? AmountInCurrency { get; set; }
        public decimal? ExchangeRate { get; set; }
    }

    public sealed class PartijLinkedCostDto
    {
        public int PartijId { get; set; }
        public string Number { get; set; }
        public string Kind { get; set; }
        public decimal CostTotal { get; set; }
    }

    // Een inkoop-partij waaruit een verkoop-partij colli pakt (20 + 120 + 360 = 500): af te leiden uit de reserveringen op palletregels
    // (of, klantgebonden, op de inkoopregel). Geen eigen verdeeltabel.
    public sealed class PartijSourceDto
    {
        public int PartijId { get; set; }
        public string Number { get; set; }
        public string VendorName { get; set; }
        public string Product { get; set; }
        public string Unit { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ShippedQuantity { get; set; }
    }

    public sealed class PartijOrderDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        // Ordernummer van de leverancier: het hoofdnummer in beeld als het er is.
        public string SupplierReference { get; set; }
        public string VendorName { get; set; }
        // Ordered, PartiallyReceived, Received, Rejected of Cancelled (zoals de inkooporderlijst).
        public string Status { get; set; }
        public DateOnly OrderedOn { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public string SupplierLotReference { get; set; }
        public string Unit { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
    }

    // Een palletregel van de partij: ontvangst, teruggeboekt na een verzendcorrectie (Correction) of afgesplitst bij afkeuring (SplitOff).
    public sealed class PartijReceiptDto
    {
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string PalletStatus { get; set; }
        public int SupplyCommitmentId { get; set; }
        public string OrderNumber { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        // Het lotnummer zoals bij ontvangst vastgelegd (kan na een nummercorrectie of bij een oudere regel afwijken van de partij).
        public string LotNumber { get; set; }
        public string Kind { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public decimal OnHandQuantity { get; set; }
        public string QualityStatus { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public sealed class PartijSalesOrderDto
    {
        public int SalesOrderId { get; set; }
        public string OrderNumber { get; set; }
        // De verkoop-partij van die order (naast het VO-nummer).
        public int? SalesPartijId { get; set; }
        public string SalesPartijNumber { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
        public string Unit { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ShippedQuantity { get; set; }
    }

    public sealed class PartijHistoryDto
    {
        public DateTime OccurredAt { get; set; }
        // Created (de partij zelf), NumberCorrected of OrderLinked.
        public string Type { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public int? SupplyCommitmentId { get; set; }
        public string ActorReference { get; set; }
    }
}
