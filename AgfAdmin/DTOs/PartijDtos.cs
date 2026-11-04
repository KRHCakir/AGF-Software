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
