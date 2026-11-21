namespace AgfAdmin.DTOs
{
    // "Klopt dit?" bij inkoop (certificaten deel 1): per product wat er aan de hand is (Kinds: NotCovered, Expired, ExpiresBeforeDelivery),
    // de GGN-certificaten van de leverancier die erbij horen en de tekst zoals die bij de bevestiging wordt vastgelegd.
    public sealed class CertificateCheckDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string SupplierName { get; set; }
        public List<string> Kinds { get; set; } = new();
        public DateOnly DeliveryDate { get; set; }
        public List<CertificateCheckGgnDto> Certificates { get; set; } = new();
        public string Message { get; set; }
    }

    public sealed class CertificateCheckGgnDto
    {
        public int CertificateId { get; set; }
        public string Number { get; set; }
        public DateOnly? ValidUntil { get; set; }
        public List<string> ProductNames { get; set; } = new();
    }
}
