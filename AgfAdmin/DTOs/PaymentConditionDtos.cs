namespace AgfAdmin.DTOs
{
    // Betalingscondities (api/payment-conditions): stamgegeven waar klant- en leveranciersrol verplicht naar verwijzen.
    public sealed class PaymentConditionDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string DescriptionNl { get; set; }
        public string DescriptionEn { get; set; }
        public int Days { get; set; }
        public string Basis { get; set; }            // InvoiceDate | EndOfMonth
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public int Version { get; set; }
        // Aantal rollen dat deze conditie gebruikt (een conditie in gebruik kan niet worden verwijderd, wel inactief).
        public int CustomerCount { get; set; }
        public int VendorCount { get; set; }
    }

    // POST (RequestId verplicht, replay op canonieke payload) en PUT (ExpectedVersion verplicht; de hele conditie wordt vervangen).
    public sealed class SavePaymentConditionDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedVersion { get; set; }
        public string Code { get; set; }
        public string DescriptionNl { get; set; }
        public string DescriptionEn { get; set; }
        public int? Days { get; set; }
        public string Basis { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }
        public bool? IsActive { get; set; }
        public int? SortOrder { get; set; }
    }
}
