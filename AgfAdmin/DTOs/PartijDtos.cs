namespace AgfAdmin.DTOs
{
    // Partijen (PartijService): nummering, aanmaken, dubbelcontrole, correctie, verwijderen en koppelen aan een inkooporder.

    public sealed class PartijSettingsDto
    {
        public string Mode { get; set; }
        public string Format { get; set; }
        public string SequenceReset { get; set; }
    }

    // Het volgende voorgestelde nummer (null in modus Own), eventueel voor een nog niet opgeslagen formaat (live voorbeeld).
    public sealed class PartijNextNumberDto
    {
        public string Mode { get; set; }
        public string Format { get; set; }
        public string SequenceReset { get; set; }
        public string Suggestion { get; set; }
    }

    public sealed class PartijSummaryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
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
        public decimal ReservedQuantity { get; set; }
        public decimal ShippedQuantity { get; set; }
        public List<PartijOrderDto> Orders { get; set; } = new();
        public List<PartijReceiptDto> Receipts { get; set; } = new();
        public List<PartijSalesOrderDto> SalesOrders { get; set; } = new();
        public List<PartijHistoryDto> History { get; set; } = new();
    }

    public sealed class PartijOrderDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
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
