namespace AgfAdmin.Models
{
    // Omzetsoort voor dienst- en vrije regels op een verkoopfactuur (verkoopfactuur 1), een korte lijst onder Beheer. De koppeling met een
    // grootboekrekening in Exact vult later alleen ExternalCode. Een omzetsoort wordt nooit verwijderd, alleen inactief gezet.
    public sealed class RevenueType
    {
        public int Id { get; set; }
        public string Name { get; set; }
        // De code van de grootboekrekening in het boekhoudpakket (later); leeg = nog niet gekoppeld.
        public string? ExternalCode { get; set; }
        public bool IsActive { get; set; } = true;
        // Precies één actieve standaard ("Omzet diensten").
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Version { get; set; }
    }
}
