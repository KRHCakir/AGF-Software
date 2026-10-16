namespace AgfAdmin.Models
{
    // Fysieke ontvangst tegen een toezegging. RequestId is uniek zodat een herhaald verzoek maar één keer telt.
    public sealed class StockReceipt
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
